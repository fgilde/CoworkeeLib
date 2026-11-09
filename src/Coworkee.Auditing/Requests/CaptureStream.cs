using System.Buffers;

namespace Coworkee.Auditing.Requests;

/// <summary>
/// Passes reads or writes through and keeps a copy of the first <c>limit</c> bytes: no buffering of the whole body, streamed answers keep streaming.
/// <paramref name="capturable"/> is asked once, at the first byte (a response's content type is known by then).
/// </summary>
internal sealed class CaptureStream(Stream inner, int limit, Func<bool>? capturable = null) : Stream
{
    private byte[]? _buffer;
    private int _length;
    private bool? _capturing;

    public Stream Inner => inner;

    public long Total { get; private set; }

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <summary>The bytes kept (null when nothing was) and whether the body was longer; gives the pooled buffer back.</summary>
    public (byte[]? Bytes, bool Truncated) Take()
    {
        var bytes = _buffer is null ? null : _buffer.AsSpan(0, _length).ToArray();
        if (_buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }

        return (bytes, Total > _length);
    }

    public override int Read(byte[] buffer, int offset, int count) => Keep(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

    public override int Read(Span<byte> buffer) => Keep(buffer[..inner.Read(buffer)]);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        return Keep(buffer.Span[..read]);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Keep(buffer.AsSpan(offset, count));
        inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Keep(buffer);
        inner.Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Keep(buffer.Span);
        return inner.WriteAsync(buffer, cancellationToken);
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    private int Keep(ReadOnlySpan<byte> data)
    {
        Total += data.Length;
        if (data.Length > 0 && _length < limit && (_capturing ??= capturable?.Invoke() ?? true))
        {
            _buffer ??= ArrayPool<byte>.Shared.Rent(limit);
            var count = Math.Min(limit - _length, data.Length);
            data[..count].CopyTo(_buffer.AsSpan(_length));
            _length += count;
        }

        return data.Length;
    }
}
