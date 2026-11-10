using System.Net;
using System.Net.Sockets;
using System.Text;
using AndroLink.Adb;

namespace AndroLink.Tests;

/// <summary>
/// Protocol-level tests against a fake adb server on the loopback interface:
/// smart-socket framing, devices parsing, transport handshake and sync STAT.
/// </summary>
public class AdbFramingTests
{
    private sealed class FakeAdbServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _acceptTask;

        public int Port { get; }
        public Func<byte[], byte[]> Responder { get; init; } = _ => Encoding.ASCII.GetBytes("OKAY");

        public FakeAdbServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptTask = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            while (true)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(); }
                catch { return; }
                _ = Task.Run(async () =>
                {
                    await using var stream = client.GetStream();
                    var buffer = new byte[4096];

                    while (true)
                    {
                        // Read 4-hex length prefix + payload
                        var header = await ReadExactlyAsync(stream, 4);
                        var requestText = Encoding.ASCII.GetString(header);
                        if (!int.TryParse(requestText, System.Globalization.NumberStyles.HexNumber, null, out var len))
                        {
                            return;
                        }
                        var payload = len > 0 ? await ReadExactlyAsync(stream, len) : Array.Empty<byte>();
                        var response = Responder(payload);
                        await stream.WriteAsync(response);
                        await stream.FlushAsync();
                        // This fake server answers one request per connection, like
                        // the real server for one-shot host: services.
                        return;
                    }
                });
            }
        }

        private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
        {
            var buffer = new byte[count];
            var read = 0;
            while (read < count)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read, count - read));
                if (n == 0) throw new EndOfStreamException();
                read += n;
            }
            return buffer;
        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }

    [Fact]
    public async Task GetServerVersionAsync_sends_framed_request_and_parses_response()
    {
        using var server = new FakeAdbServer
        {
            Responder = request =>
            {
                Assert.Equal("host:version", Encoding.UTF8.GetString(request));
                return Encoding.ASCII.GetBytes("OKAY00040031"); // OKAY + 4-hex-len + "0031"
            },
        };

        var client = new AdbClient { Port = server.Port };
        var version = await client.GetServerVersionAsync();
        Assert.Equal("0031", version);
    }

    [Fact]
    public async Task ListDevicesAsync_parses_serial_and_state()
    {
        using var server = new FakeAdbServer
        {
            Responder = request =>
            {
                Assert.Equal("host:devices", Encoding.UTF8.GetString(request));
                var text = "List of devices attached\r\nemulator-5554\tdevice\r\n0123456789abcdef\tunauthorized\r\n";
                return Encoding.ASCII.GetBytes("OKAY").Concat(Encoding.UTF8.GetBytes(text)).ToArray();
            },
        };

        var client = new AdbClient { Port = server.Port };
        var devices = await client.ListDevicesAsync();

        Assert.Equal(2, devices.Count);
        Assert.Equal("emulator-5554", devices[0].Serial);
        Assert.Equal("device", devices[0].State);
        Assert.Equal("0123456789abcdef", devices[1].Serial);
        Assert.Equal("unauthorized", devices[1].State);
    }

    [Fact]
    public async Task Fail_response_surfaces_as_AdbException()
    {
        using var server = new FakeAdbServer
        {
            Responder = _ => Encoding.ASCII.GetBytes("FAIL000cunknown host"),
        };

        var client = new AdbClient { Port = server.Port };
        var ex = await Assert.ThrowsAsync<AdbException>(() => client.GetServerVersionAsync());
        Assert.Contains("unknown host", ex.Message);
    }

    [Fact]
    public async Task SyncClient_performs_stat_over_transport_handshake()
    {
        // One connection must answer TWO requests (transport + sync), then sync frames.
        using var server = new FakeSyncServer();

        var client = new AdbClient { Port = server.Port };
        using var sync = await client.OpenSyncAsync(null);
        var stat = await sync.StatAsync("/sdcard/DCIM/photo.jpg");

        Assert.Equal(0x81A4u, stat.Mode);   // 0100644 octal
        Assert.Equal(123456u, stat.Size);
        Assert.Equal(1700000000u, stat.Mtime);
        Assert.False(stat.IsDirectory);
    }

    private sealed class FakeSyncServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public int Port { get; }

        public FakeSyncServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();

            await ExpectRequestAsync(stream, "host:transport-any");
            await stream.WriteAsync(Encoding.ASCII.GetBytes("OKAY"));

            await ExpectRequestAsync(stream, "sync:");
            await stream.WriteAsync(Encoding.ASCII.GetBytes("OKAY"));

            // sync STAT request: "STAT" + u32 LE length + path
            var id = await ReadExactlyAsync(stream, 4);
            Assert.Equal("STAT", Encoding.ASCII.GetString(id));
            var lenBytes = await ReadExactlyAsync(stream, 4);
            var len = BitConverter.ToUInt32(lenBytes, 0);
            var path = Encoding.UTF8.GetString(await ReadExactlyAsync(stream, (int)len));
            Assert.Equal("/sdcard/DCIM/photo.jpg", path);

            var response = new List<byte>();
            response.AddRange(Encoding.ASCII.GetBytes("STAT"));
            response.AddRange(BitConverter.GetBytes(0x81A4u));
            response.AddRange(BitConverter.GetBytes(123456u));
            response.AddRange(BitConverter.GetBytes(1700000000u));
            await stream.WriteAsync(response.ToArray());
        }

        private static async Task ExpectRequestAsync(NetworkStream stream, string expected)
        {
            var header = await ReadExactlyAsync(stream, 4);
            var len = Convert.ToInt32(Encoding.ASCII.GetString(header), 16);
            var payload = Encoding.UTF8.GetString(await ReadExactlyAsync(stream, len));
            Assert.Equal(expected, payload);
        }

        private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
        {
            var buffer = new byte[count];
            var read = 0;
            while (read < count)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read, count - read));
                if (n == 0) throw new EndOfStreamException();
                read += n;
            }
            return buffer;
        }

        public void Dispose() => _listener.Stop();
    }
}
