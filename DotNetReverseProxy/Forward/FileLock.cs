namespace DotNetReverseProxy.Forward;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

public class LockFile : IDisposable
{
    private static readonly string LockFolder = Path.Combine(Path.GetTempPath(), "locks");
    private const int Seconds = 1000;
    private const int Minutes = 60 * Seconds;
    private const int Hours = 60 * Minutes;

    private readonly string _lockFile;
    private readonly Timer _timer;

    private LockFile(string lockFile)
    {
        _lockFile = lockFile;
        locked = true;

        File.WriteAllText(_lockFile, "locked");
        _timer = new Timer(UpdateTimestamp, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    public bool locked { get; private set; }

    public static async Task<IDisposable> LockAsync(string someKey, int timeout = 2 * Hours, int interval = 3000, bool throwOnFail = true)
    {
        if (!Directory.Exists(LockFolder))
        {
            Directory.CreateDirectory(LockFolder);
        }

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(someKey));
        var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        var lockFile = Path.Combine(LockFolder, $"{hash}.lock");

        var till = DateTime.UtcNow.AddMilliseconds(timeout);
        Exception lastError = null;

        while (DateTime.UtcNow < till)
        {
            try
            {
                if (!File.Exists(lockFile))
                {
                    return new LockFile(lockFile);
                }

                var lastWriteTime = File.GetLastWriteTimeUtc(lockFile);
                var past = DateTime.UtcNow.AddSeconds(-15);
                if (lastWriteTime < past)
                {
                    File.Delete(lockFile); // stale lock
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(interval);
        }

        if (throwOnFail)
        {
            throw new TimeoutException($"Could not acquire lock {lockFile}. Reason: {lastError?.Message ?? "Timeout"}");
        }

        return new LockFilePlaceholder();
    }

    private void UpdateTimestamp(object _)
    {
        try
        {
            File.SetLastWriteTimeUtc(_lockFile, DateTime.UtcNow);
        }
        catch
        {
            // Ignore
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        try
        {
            if (File.Exists(_lockFile))
                File.Delete(_lockFile);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
    }
}

// Placeholder class when locking fails and throwOnFail is false
public class LockFilePlaceholder : IDisposable
{
    public bool locked => false;

    public void Dispose() { }
}