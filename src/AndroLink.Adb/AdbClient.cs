using System.Text;

namespace AndroLink.Adb;

public sealed record AdbDeviceInfo(string Serial, string State);

/// <summary>
/// High-level pure-C# ADB client. Speaks to a standard adb server (127.0.0.1:5037)
/// exactly like the adb CLI: enumerate devices, bind to a device transport,
/// run shell commands and open sync: sessions for file transfer.
/// </summary>
public sealed class AdbClient : IDisposable
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = AdbConnection.DefaultPort;

    private bool _verified;

    public async Task EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (_verified) return;
        using var conn = await AdbConnection.ConnectAsync(Host, Port, ct: ct);
        await conn.SendAsync("host:version", ct);
        await conn.ExpectOkayAsync(ct);
        await conn.ReadLengthPrefixedStringAsync(ct); // server version
        _verified = true;
    }

    public async Task<string> GetServerVersionAsync(CancellationToken ct = default)
    {
        using var conn = await AdbConnection.ConnectAsync(Host, Port, ct: ct);
        await conn.SendAsync("host:version", ct);
        await conn.ExpectOkayAsync(ct);
        return await conn.ReadLengthPrefixedStringAsync(ct);
    }

    /// <summary>Lists devices as `adb devices` would: serial + state.</summary>
    public async Task<List<AdbDeviceInfo>> ListDevicesAsync(CancellationToken ct = default)
    {
        using var conn = await AdbConnection.ConnectAsync(Host, Port, ct: ct);
        await conn.SendAsync("host:devices", ct);
        await conn.ExpectOkayAsync(ct);
        var text = Encoding.UTF8.GetString(await conn.ReadToEndAsync(ct));

        var devices = new List<AdbDeviceInfo>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith("List of devices", StringComparison.Ordinal) ||
                line.StartsWith('*'))
            {
                continue;
            }
            var parts = line.Split('\t', 2);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
            {
                devices.Add(new AdbDeviceInfo(parts[0].Trim(), parts[1].Trim()));
            }
        }
        return devices;
    }

    /// <summary>
    /// Opens a connection bound to the device and streams the requested
    /// service (e.g. "shell:ls /sdcard"). Returns everything the device sends
    /// until the connection closes.
    /// </summary>
    public async Task<byte[]> DeviceServiceAsync(string? serial, string service, CancellationToken ct = default)
    {
        using var conn = await AdbConnection.ConnectAsync(Host, Port, ct: ct);
        await conn.SendAsync($"host:{(string.IsNullOrEmpty(serial) ? "transport-any" : $"transport:{serial}")}", ct);
        await conn.ExpectOkayAsync(ct);
        await conn.SendAsync(service, ct);
        await conn.ExpectOkayAsync(ct);
        return await conn.ReadToEndAsync(ct);
    }

    /// <summary>
    /// Runs a shell command and returns its output. Exit status is captured by
    /// appending an echo marker - works on every adbd, old and new.
    /// </summary>
    public async Task<string> RunShellAsync(string? serial, string command, CancellationToken ct = default)
    {
        const string marker = "__AL_RC_";
        var wrapped = $"{command}; echo {marker}$?";
        var output = await DeviceServiceAsync(serial, "shell:" + wrapped, ct);
        var text = CleanOutput(output);

        var idx = text.LastIndexOf(marker, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var tail = text[(idx + marker.Length)..].Trim();
            text = text[..idx].TrimEnd();
            if (tail.Length > 0 && !tail.StartsWith('0'))
            {
                // non-zero exit: keep the message short but informative
                throw new AdbException($"command failed (exit {tail}): {Truncate(command)}");
            }
        }
        return text;
    }

    /// <summary>Opens a sync: session bound to the device for file transfer.</summary>
    public async Task<AdbSyncClient> OpenSyncAsync(string? serial, CancellationToken ct = default)
    {
        var conn = await AdbConnection.ConnectAsync(Host, Port, ct: ct);
        try
        {
            await conn.SendAsync($"host:{(string.IsNullOrEmpty(serial) ? "transport-any" : $"transport:{serial}")}", ct);
            await conn.ExpectOkayAsync(ct);
            await conn.SendAsync("sync:", ct);
            await conn.ExpectOkayAsync(ct);
            return new AdbSyncClient(conn);
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    internal static string CleanOutput(byte[] raw)
    {
        // adb historically emits \r\n on some transports; normalize.
        return Encoding.UTF8.GetString(raw).Replace("\r\n", "\n").TrimEnd('\n');
    }

    private static string Truncate(string s) => s.Length <= 80 ? s : s[..77] + "...";

    public void Dispose() { /* stateless per-operation connections */ }
}
