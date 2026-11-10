using System.Diagnostics;
using AndroLink.Adb;
using AndroLink.Core;
using AndroLink.Core.SystemImages;

namespace AndroLink.Backend.Qemu;

/// <summary>
/// Reference IRuntimeBackend plugin: launches an Android-x86 virtual machine
/// in QEMU, bridges it to AndroLink over ADB (hostfwd tcp::5555), and then
/// delegates every device operation (install, shell, file mirror) to an
/// embedded AdbBackend bound to serial 127.0.0.1:5555.
///
/// Architecture support: picks qemu-system-x86_64 for x86/x86_64 guests and
/// qemu-system-aarch64 for arm64-v8a/armeabi-v7a guests, so both x64 PCs and
/// ARM64 (Windows-on-ARM) devices are covered. Cross-architecture runs are
/// possible but execute under TCG full emulation (slow) - a warning is logged.
///
/// Backend options (DeviceConfig.BackendOptions):
///   qemuPath   - path to qemu-system-* (default: PATH / Program Files\qemu)
///   imagePath  - path to the Android-x86 disk image (qcow2/vdi/vmdk) or ISO
///   extraArgs  - appended verbatim to the QEMU command line
///   bootTimeoutSec - boot wait (default 180; first Android-x86 boot is slow)
///
/// Note: downloaded AOSP/Google emulator images (system-images;...) need the
/// "Android emulator (SDK images)" backend instead - they are built for the
/// emulator's goldfish device model which vanilla QEMU does not implement.
///
/// This project is the worked example for docs/PLUGIN_AUTHORING.md. It is
/// experimental: the shared /sdcard folder works as an ADB-synced MIRROR
/// (like AdbBackend). The roadmap design is a 9p/virtfs bind mount of the
/// same Windows folder, which makes it live exactly like the simulated
/// backend - see docs/FILE_ACCESS.md.
/// </summary>
public sealed class QemuBackend : IRuntimeBackend
{
    private const string DefaultSerial = "127.0.0.1:5555";

    private BackendContext? _context;
    private readonly AdbBackend _adb = new();
    private Process? _qemuProcess;

    public string Id => "qemu";
    public string DisplayName => "QEMU Android-x86 (experimental)";
    public string Description => "Launches an Android-x86 VM in QEMU and bridges it over ADB. Requires a user-supplied Android-x86 image.";

    public DeviceCapabilities Capabilities { get; } = new()
    {
        SupportsInstall = true,
        SupportsShell = true,
        SupportsFileSync = true,
        LiveSharedFolder = false,
        SupportsGApps = false,
    };

    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;
    public event Action<DeviceConfig, string>? LogEmitted;

    public void Initialize(BackendContext context)
    {
        _context = context;
        _adb.Initialize(context);
        _adb.StateChanged += (s, e) => StateChanged?.Invoke(s, e);
        _adb.LogEmitted += (d, m) => LogEmitted?.Invoke(d, m);
    }

    public async Task StartAsync(DeviceConfig device, CancellationToken ct = default)
    {
        var options = device.BackendOptions;
        var imagePath = options.GetValueOrDefault("imagePath", device.ImagePath ?? "");
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            throw new InvalidOperationException(
                $"QEMU backend needs an Android-x86 image (set imagePath; got '{imagePath}')");
        }

        if (_qemuProcess is { HasExited: false })
        {
            LogEmitted?.Invoke(device, "QEMU already running - reusing the existing VM");
        }
        else
        {
            LaunchQemu(device, imagePath, options);
        }

        // Bridge the VM into the adb server, then wait for boot through the
        // embedded ADB backend (progress + state events flow from there).
        var client = new AdbClient();
        await AdbServer.EnsureServerRunningAsync(options.GetValueOrDefault("adbPath"), ct);
        await client.DeviceServiceAsync(null, $"host:connect:{DefaultSerial}", ct);
        LogEmitted?.Invoke(device, $"adb bridge established at {DefaultSerial}");

        var adbDevice = WithSerial(device, options);
        await _adb.StartAsync(adbDevice, ct);
    }

    public Task StopAsync(DeviceConfig device, CancellationToken ct = default)
    {
        var adbDevice = WithSerial(device, device.BackendOptions);
        try
        {
            // Ask Android to power off gracefully; then hard-kill if needed.
            var client = new AdbClient();
            try
            {
                _ = client.RunShellAsync(DefaultSerial, "reboot -p", CancellationToken.None);
            }
            catch { /* VM may already be gone */ }

            _adb.StopAsync(adbDevice, ct).Wait(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (_qemuProcess is { HasExited: false })
            {
                if (!_qemuProcess.WaitForExitAsync(CancellationToken.None).Wait(8000))
                {
                    LogEmitted?.Invoke(device, "QEMU did not exit in time - killing");
                    try { _qemuProcess.Kill(entireProcessTree: true); }
                    catch { }
                }
            }
            _qemuProcess?.Dispose();
            _qemuProcess = null;
        }
        return Task.CompletedTask;
    }

    public Task<BackendStatus> GetStatusAsync(DeviceConfig device, CancellationToken ct = default)
        => _adb.GetStatusAsync(WithSerial(device, device.BackendOptions), ct);

    public Task InstallAppAsync(DeviceConfig device, string apkPath, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
        => _adb.InstallAppAsync(WithSerial(device, device.BackendOptions), apkPath, progress, ct);

    public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(DeviceConfig device, CancellationToken ct = default)
        => _adb.ListInstalledAppsAsync(WithSerial(device, device.BackendOptions), ct);

    public Task UninstallAppAsync(DeviceConfig device, string packageName, CancellationToken ct = default)
        => _adb.UninstallAppAsync(WithSerial(device, device.BackendOptions), packageName, ct);

    public Task SyncFromDeviceAsync(DeviceConfig device, string androidPath, string windowsDir, CancellationToken ct = default)
        => _adb.SyncFromDeviceAsync(WithSerial(device, device.BackendOptions), androidPath, windowsDir, ct);

    public Task PushToDeviceAsync(DeviceConfig device, string windowsDir, string androidPath, CancellationToken ct = default)
        => _adb.PushToDeviceAsync(WithSerial(device, device.BackendOptions), windowsDir, androidPath, ct);

    public Task<string?> ResolveAdbSerialAsync(DeviceConfig device, CancellationToken ct = default)
        => Task.FromResult<string?>(DefaultSerial);

    // ------------------------------------------------------------ internals

    private static DeviceConfig WithSerial(DeviceConfig device, IReadOnlyDictionary<string, string> options)
    {
        var clone = CloneDevice(device);
        clone.BackendOptions["serial"] = options.GetValueOrDefault("serial", DefaultSerial);
        return clone;
    }

    private static DeviceConfig CloneDevice(DeviceConfig device) => new()
    {
        Id = device.Id,
        Name = device.Name,
        Width = device.Width,
        Height = device.Height,
        Dpi = device.Dpi,
        RamMb = device.RamMb,
        CpuCores = device.CpuCores,
        BackendId = device.BackendId,
        ImagePath = device.ImagePath,
        ApiLevel = device.ApiLevel,
        Abi = device.Abi,
        ImagePackage = device.ImagePackage,
        GAppsZipPath = device.GAppsZipPath,
        BackendOptions = new Dictionary<string, string>(device.BackendOptions, StringComparer.OrdinalIgnoreCase),
        Notes = device.Notes,
        CreatedAt = device.CreatedAt,
    };

    private void LaunchQemu(DeviceConfig device, string imagePath, IReadOnlyDictionary<string, string> options)
    {
        var guestArch = GuestArchitecture(device);
        var qemuPath = ResolveQemuExecutable(options.GetValueOrDefault("qemuPath"), guestArch);
        var extraArgs = options.GetValueOrDefault("extraArgs", "");

        WarnOnCrossArchitecture(device, guestArch);

        // Try the fast accelerator first, always fall back to TCG.
        var accelerators = OperatingSystem.IsWindows()
            ? (HostInfo.IsArmHost ? new[] { "tcg" } : new[] { "whpx", "tcg" })
            : new[] { "kvm", "tcg" };

        var args = new List<string>();
        foreach (var accel in accelerators)
        {
            args.Add("-accel");
            args.Add(accel);
        }
        args.AddRange(new[]
        {
            "-m", device.RamMb.ToString(),
            "-smp", device.CpuCores.ToString(),
            "-drive", $"file=\"{imagePath}\",format=qcow2,if=virtio",
            "-nic", "user,hostfwd=tcp:127.0.0.1:5555-:5555",
            "-device", "virtio-tablet",
            "-display", "sdl",
            "-audio", "none",
        });

        if (guestArch == GuestArch.Arm64)
        {
            // qemu-system-aarch64 needs an explicit machine and firmware for
            // UEFI-bootable Android-x86 style arm64 media.
            args.InsertRange(0, new[] { "-M", "virt" });
        }

        if (device.GAppsZipPath is not null)
        {
            LogEmitted?.Invoke(device,
                "GApps hook: sideload the openGApps zip after first boot (recovery or adb sideload); " +
                "QEMU backend does not flash images itself");
        }

        if (!string.IsNullOrWhiteSpace(extraArgs))
        {
            args.Add(extraArgs);
        }

        var psi = new ProcessStartInfo
        {
            FileName = qemuPath,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        LogEmitted?.Invoke(device, $"launching {qemuPath} {string.Join(' ', args)}");
        _qemuProcess = Process.Start(psi) ?? throw new InvalidOperationException("failed to start QEMU");
    }

    internal enum GuestArch { X64, Arm64 }

    /// <summary>Guest CPU architecture implied by the device ABI (default: x64).</summary>
    internal static GuestArch GuestArchitecture(DeviceConfig device)
    {
        var abi = device.Abi;
        if (string.IsNullOrEmpty(abi)) abi = device.ImagePackage?.Split(';') is { Length: 4 } parts ? parts[3] : "";
        return CpuAbi.IsArm(abi) ? GuestArch.Arm64 : GuestArch.X64;
    }

    private static void WarnOnCrossArchitecture(DeviceConfig device, GuestArch guestArch)
    {
        var guestIsArm = guestArch == GuestArch.Arm64;
        var hostIsArm = HostInfo.IsArmHost;
        if (guestIsArm != hostIsArm)
        {
            Log.Warn("qemu", $"device '{device.Name}': guest ABI {device.Abi} runs under TCG full emulation on this {HostInfo.ArchitectureLabel} host (slow).");
        }
    }

    internal static string QemuBinaryName(GuestArch arch)
        => "qemu-system-" + (arch == GuestArch.Arm64 ? "aarch64" : "x86_64")
           + (OperatingSystem.IsWindows() ? ".exe" : "");

    internal static string ResolveQemuExecutable(string? configured, GuestArch arch)
    {
        var primary = QemuBinaryName(arch);
        var fallback = QemuBinaryName(arch == GuestArch.Arm64 ? GuestArch.X64 : GuestArch.Arm64);

        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in new[] { primary, fallback })
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var name in new[] { primary, fallback })
            {
                var candidate = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "qemu", name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        throw new InvalidOperationException(
            $"{primary} was not found. Install QEMU (https://qemu.org) or set the qemuPath backend option.");
    }

    public void Dispose()
    {
        try
        {
            if (_qemuProcess is { HasExited: false })
            {
                _qemuProcess.Kill(entireProcessTree: true);
            }
        }
        catch { }
        _qemuProcess?.Dispose();
    }
}
