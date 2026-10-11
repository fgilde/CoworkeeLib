using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Coworkee.Infrastructure.Locking;

/// <summary>An exclusive open of a file per database and name; the operating system releases it when the process ends.</summary>
internal sealed class FileLock(FileStream file) : IAsyncDisposable
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "coworkee-locks");

    public static FileLock? TryAcquire(string connectionString, string name)
    {
        var source = new SqliteConnectionStringBuilder(connectionString).DataSource;
        var database = source is "" or ":memory:" ? source : Path.GetFullPath(source);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{database}|{name}")))[..32];
        Directory.CreateDirectory(Folder);
        try
        {
            // no DeleteOnClose: a file removed while another process opens it would hand out the lock twice
            return new FileLock(new FileStream(Path.Combine(Folder, key + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public ValueTask DisposeAsync() => file.DisposeAsync();
}
