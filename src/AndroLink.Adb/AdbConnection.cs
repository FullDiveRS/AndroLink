using System.Net.Sockets;
using System.Text;

namespace AndroLink.Adb;

/// <summary>Error raised for ADB protocol or device failures.</summary>
public class AdbException : Exception
{
    public AdbException(string message) : base(message) { }
    public AdbException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// A single connection to the adb server using the "smart socket" protocol:
/// requests are a 4-hex-digit length prefix followed by the service name
/// (e.g. <c>host:devices</c>, <c>host:transport:emulator-5554</c>, then
/// <c>shell:...</c> or <c>sync:</c>). This is the same protocol the adb CLI
/// itself uses, so it works with any standard adb server.
/// </summary>
public sealed class AdbConnection : IDisposable
{
    public const int DefaultPort = 5037;

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    private AdbConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public string Host { get; private init; } = "127.0.0.1";
    public int Port { get; private init; } = DefaultPort;

    public static async Task<AdbConnection> ConnectAsync(
        string host = "127.0.0.1", int port = DefaultPort, int timeoutMs = 5000, CancellationToken ct = default)
    {
        var client = new TcpClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timeoutMs);
            await client.ConnectAsync(host, port, timeout.Token);
            var conn = new AdbConnection(client) { Host = host, Port = port };
            return conn;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new AdbException($"timed out connecting to adb server at {host}:{port}");
        }
        catch (SocketException ex)
        {
            client.Dispose();
            throw new AdbException($"cannot reach adb server at {host}:{port} ({ex.Message})", ex);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task SendAsync(string payload, CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var header = Encoding.ASCII.GetBytes(bytes.Length.ToString("X4"));
        await _stream.WriteAsync(header, ct);
        await _stream.WriteAsync(bytes, ct);
        await _stream.FlushAsync(ct);
    }

    /// <summary>Reads the OKAY/FAIL status of the current request; throws on FAIL.</summary>
    public async Task ExpectOkayAsync(CancellationToken ct = default)
    {
        var status = await ReadExactlyAsync(4, ct);
        var code = Encoding.ASCII.GetString(status);
        if (code == "FAIL")
        {
            throw new AdbException(await ReadLengthPrefixedStringAsync(ct));
        }
        if (code != "OKAY")
        {
            throw new AdbException($"unexpected adb response '{code}' (expected OKAY)");
        }
    }

    /// <summary>Reads a 4-hex-digit length prefix then that many bytes as text.</summary>
    public async Task<string> ReadLengthPrefixedStringAsync(CancellationToken ct = default)
    {
        var header = Encoding.ASCII.GetString(await ReadExactlyAsync(4, ct));
        if (!int.TryParse(header, System.Globalization.NumberStyles.HexNumber, null, out var len))
        {
            throw new AdbException($"malformed length prefix '{header}'");
        }
        var payload = await ReadExactlyAsync(len, ct);
        return Encoding.UTF8.GetString(payload);
    }

    public async Task<byte[]> ReadExactlyAsync(int count, CancellationToken ct = default)
    {
        if (count < 0) throw new AdbException($"negative read length {count}");
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = await _stream.ReadAsync(buffer.AsMemory(read, count - read), ct);
            if (n == 0) throw new EndOfStreamException($"adb connection closed after {read}/{count} bytes");
            read += n;
        }
        return buffer;
    }

    /// <summary>Reads the stream until the server closes the connection.</summary>
    public async Task<byte[]> ReadToEndAsync(CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int n;
        while ((n = await _stream.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    /// <summary>The raw stream, for services like sync: that speak their own protocol afterwards.</summary>
    public NetworkStream RawStream => _stream;

    public void Dispose()
    {
        _stream.Dispose();
        _client.Dispose();
    }
}
