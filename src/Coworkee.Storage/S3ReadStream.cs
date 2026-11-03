using Amazon.S3;
using Amazon.S3.Model;

namespace Coworkee.Storage;

/// <summary>Seekable read over an S3 object: each read after a seek opens a ranged GET from the current position.</summary>
internal sealed class S3ReadStream(IAmazonS3 client, string bucket, string key, long length) : Stream
{
    private Stream? _body;
    private GetObjectResponse? _response;
    private long _position;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= length || buffer.Length == 0)
        {
            return 0;
        }

        if (_body is null)
        {
            _response = await client.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = key, ByteRange = new ByteRange(_position, length - 1) }, cancellationToken);
            _body = _response.ResponseStream;
        }

        var read = await _body.ReadAsync(buffer, cancellationToken);
        _position += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset,
        };
        ArgumentOutOfRangeException.ThrowIfNegative(target, nameof(offset));
        if (target != _position)
        {
            CloseBody();
            _position = target;
        }

        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseBody();
        }

        base.Dispose(disposing);
    }

    private void CloseBody()
    {
        _body?.Dispose();
        _response?.Dispose();
        _body = null;
        _response = null;
    }
}
