using System.Text;

namespace AndroLink.Adb;

public readonly record struct SyncStat(uint Mode, ulong Size, uint Mtime)
{
    public bool IsDirectory => (Mode & 0xF000u) == 0x4000u;
}

public sealed record SyncEntry(string Name, uint Mode, ulong Size, uint Mtime)
{
    public bool IsDirectory => (Mode & 0xF000u) == 0x4000u;
}

/// <summary>
/// Implements the ADB "sync:" sub-protocol (v1) on top of an already-bound
/// AdbConnection: STAT, LIST, file SEND/RECV and recursive directory mirroring.
/// This is how `adb pull`/`adb push` move files.
/// </summary>
public sealed class AdbSyncClient : IDisposable
{
    private const uint DataChunkSize = 32 * 1024;

    private readonly AdbConnection _conn;

    internal AdbSyncClient(AdbConnection conn) => _conn = conn;

    public async Task<SyncStat> StatAsync(string remotePath, CancellationToken ct = default)
    {
        await SendRequestAsync("STAT", remotePath, ct);
        var id = await ReadIdAsync(ct);
        if (id == "FAIL") throw new AdbException(await ReadFailMessageAsync(ct));
        if (id != "STAT") throw new AdbException($"unexpected sync response '{id}' for STAT");
        var mode = await ReadU32Async(ct);
        var size = await ReadU32Async(ct);
        var mtime = await ReadU32Async(ct);
        return new SyncStat(mode, size, mtime);
    }

    public async Task<List<SyncEntry>> ListAsync(string remotePath, CancellationToken ct = default)
    {
        await SendRequestAsync("LIST", remotePath, ct);
        var entries = new List<SyncEntry>();
        while (true)
        {
            var id = await ReadIdAsync(ct);
            switch (id)
            {
                case "DENT":
                    var mode = await ReadU32Async(ct);
                    var size = await ReadU32Async(ct);
                    var mtime = await ReadU32Async(ct);
                    var nameLen = (int)await ReadU32Async(ct);
                    var name = Encoding.UTF8.GetString(await _conn.ReadExactlyAsync(nameLen, ct));
                    if (name != "." && name != "..") entries.Add(new SyncEntry(name, mode, size, mtime));
                    break;

                case "DONE":
                    return entries;

                case "FAIL":
                    throw new AdbException(await ReadFailMessageAsync(ct));

                default:
                    throw new AdbException($"unexpected sync response '{id}' during LIST");
            }
        }
    }

    public async Task PushFileAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        await using var input = File.OpenRead(localPath);
        // SEND request: "path,mode" - mode 0100644 = S_IFREG | rw-r--r-- (octal, parsed by adbd with base 0)
        var payload = $"{remotePath},0100644";
        await SendRequestAsync("SEND", payload, ct);

        var buffer = new byte[DataChunkSize];
        long sent = 0;
        int n;
        while ((n = await input.ReadAsync(buffer, ct)) > 0)
        {
            await SendIdLengthAsync("DATA", n, ct);
            await _conn.RawStream.WriteAsync(buffer.AsMemory(0, n), ct);
            sent += n;
            progress?.Report(sent);
        }

        var mtime = (uint)Math.Min(DateTimeOffset.Now.ToUnixTimeSeconds(), uint.MaxValue);
        await SendIdLengthAsync("DONE", (int)mtime, ct);

        await FinishAsync(ct);
    }

    public async Task PullFileAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        await SendRequestAsync("RECV", remotePath, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(localPath))!);
        await using var output = File.Create(localPath);
        long received = 0;

        while (true)
        {
            var id = await ReadIdAsync(ct);
            if (id == "DATA")
            {
                var len = (int)await ReadU32Async(ct);
                var chunk = await _conn.ReadExactlyAsync(len, ct);
                await output.WriteAsync(chunk, ct);
                received += len;
                progress?.Report(received);
            }
            else if (id == "DONE")
            {
                await output.FlushAsync(ct);
                return;
            }
            else if (id == "FAIL")
            {
                throw new AdbException(await ReadFailMessageAsync(ct));
            }
            else
            {
                throw new AdbException($"unexpected sync response '{id}' during RECV");
            }
        }
    }

    /// <summary>Recursively mirrors a local directory tree to the device (mkdir -p per folder, then file SENDs).</summary>
    public async Task PushDirectoryAsync(string localDir, string remoteDir, IProgress<string>? log = null, CancellationToken ct = default)
    {
        await PushDirectoryRecursive(localDir, NormalizeRemote(remoteDir), log, ct);
    }

    private async Task PushDirectoryRecursive(string localDir, string remoteDir, IProgress<string>? log, CancellationToken ct)
    {
        log?.Report($"mkdir {remoteDir}");
        await _conn.RawStream.FlushAsync(ct);
        await MkdirRemoteAsync(remoteDir, ct);

        foreach (var file in Directory.EnumerateFiles(localDir))
        {
            var target = remoteDir + "/" + Path.GetFileName(file).Replace('\\', '/');
            log?.Report($"push {target}");
            await PushFileAsync(file, target, null, ct);
        }

        foreach (var dir in Directory.EnumerateDirectories(localDir))
        {
            var target = remoteDir + "/" + Path.GetFileName(dir).Replace('\\', '/');
            await PushDirectoryRecursive(dir, target, log, ct);
        }
    }

    /// <summary>Recursively mirrors a remote directory tree to Windows.</summary>
    public async Task PullDirectoryAsync(string remoteDir, string localDir, IProgress<string>? log = null, CancellationToken ct = default)
    {
        await PullDirectoryRecursive(NormalizeRemote(remoteDir), localDir, log, ct);
    }

    private async Task PullDirectoryRecursive(string remoteDir, string localDir, IProgress<string>? log, CancellationToken ct)
    {
        Directory.CreateDirectory(localDir);
        foreach (var entry in await ListAsync(remoteDir, ct))
        {
            if (entry.IsDirectory)
            {
                await PullDirectoryRecursive(remoteDir + "/" + entry.Name, Path.Combine(localDir, entry.Name), log, ct);
            }
            else
            {
                var target = Path.Combine(localDir, entry.Name);
                log?.Report($"pull {remoteDir}/{entry.Name} -> {target}");
                await PullFileAsync(remoteDir + "/" + entry.Name, target, null, ct);
            }
        }
    }

    private async Task MkdirRemoteAsync(string remoteDir, CancellationToken ct)
    {
        // mkdir -p via shell on a separate connection keeps the sync session simple.
        using var conn = await AdbConnection.ConnectAsync(_conn.Host, _conn.Port, ct: ct);
        await conn.SendAsync("host:transport-any", ct);
        await conn.ExpectOkayAsync(ct);
        await conn.SendAsync($"shell:mkdir -p '{remoteDir.Replace("'", @"'\''")}'", ct);
        await conn.ExpectOkayAsync(ct);
        await conn.ReadToEndAsync(ct);
    }

    private static string NormalizeRemote(string path) => "/" + path.Trim().Replace('\\', '/').TrimStart('/');

    // ------------------------------------------------------------ wire level

    private async Task SendRequestAsync(string id, string payload, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        await SendIdLengthAsync(id, bytes.Length, ct);
        await _conn.RawStream.WriteAsync(bytes, ct);
    }

    private async Task SendIdLengthAsync(string id, int length, CancellationToken ct)
    {
        var buffer = new byte[8];
        buffer[0] = (byte)id[0];
        buffer[1] = (byte)id[1];
        buffer[2] = (byte)id[2];
        buffer[3] = (byte)id[3];
        WriteU32LE(buffer.AsSpan(4), (uint)length);
        await _conn.RawStream.WriteAsync(buffer, ct);
    }

    private async Task<string> ReadIdAsync(CancellationToken ct)
        => Encoding.ASCII.GetString(await _conn.ReadExactlyAsync(4, ct));

    private async Task<uint> ReadU32Async(CancellationToken ct)
    {
        var b = await _conn.ReadExactlyAsync(4, ct);
        return (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));
    }

    private async Task FinishAsync(CancellationToken ct)
    {
        var id = await ReadIdAsync(ct);
        if (id == "FAIL") throw new AdbException(await ReadFailMessageAsync(ct));
        if (id != "OKAY" && id != "DONE") throw new AdbException($"unexpected sync response '{id}' after SEND");
    }

    private async Task<string> ReadFailMessageAsync(CancellationToken ct)
    {
        var len = (int)await ReadU32Async(ct);
        var msg = Encoding.UTF8.GetString(await _conn.ReadExactlyAsync(len, ct));
        return $"device rejected sync operation: {msg}";
    }

    private static void WriteU32LE(Span<byte> span, uint value)
    {
        span[0] = (byte)value;
        span[1] = (byte)(value >> 8);
        span[2] = (byte)(value >> 16);
        span[3] = (byte)(value >> 24);
    }

    public void Dispose() => _conn.Dispose();
}
