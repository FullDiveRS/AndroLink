# Writing a runtime backend plugin

A **runtime backend** is the component that actually makes an Android instance
exist: boot it, report its state, install apps, move files. AndroLink ships
three (simulated, ADB bridge, QEMU reference) and loads any number of custom
ones from DLLs at startup. This guide walks through writing one.

## 1. The contract

```csharp
namespace AndroLink.Core;

public interface IRuntimeBackend : IDisposable
{
    string Id { get; }                        // stable id, e.g. "qemu"
    string DisplayName { get; }               // shown in pickers
    string Description => "";
    DeviceCapabilities Capabilities { get; }

    event EventHandler<DeviceStateChangedEventArgs> StateChanged;  // (deviceId, status)
    event Action<DeviceConfig, string> LogEmitted;                 // diagnostics

    void Initialize(BackendContext context);  // once, before first use

    Task StartAsync(DeviceConfig device, CancellationToken ct = default);
    Task StopAsync(DeviceConfig device, CancellationToken ct = default);
    Task<BackendStatus> GetStatusAsync(DeviceConfig device, CancellationToken ct = default);

    Task InstallAppAsync(DeviceConfig device, string apkPath,
        IProgress<InstallProgress>? progress = null, CancellationToken ct = default);
    Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(DeviceConfig device, ...);
    Task UninstallAppAsync(DeviceConfig device, string packageName, ...);

    Task SyncFromDeviceAsync(DeviceConfig device, string androidPath, string windowsDir, ...);
    Task PushToDeviceAsync(DeviceConfig device, string windowsDir, string androidPath, ...);
    Task<string?> ResolveAdbSerialAsync(DeviceConfig device, ...);
}
```

### DeviceCapabilities

```csharp
new DeviceCapabilities
{
    SupportsInstall = true,   // InstallAppAsync works
    SupportsShell = true,     // backend exposes a shell (adbd-level)
    SupportsFileSync = true,  // Sync/Push work (mirror mode)
    LiveSharedFolder = true,  // the Windows sdcard folder IS device storage
    SupportsGApps = true,     // understands the openGApps hook
}
```

The Manager reads these: no `SupportsInstall` → the Apps page disables
installing; `LiveSharedFolder` → the Files page hides sync buttons and says
"Live shared folder: edits apply immediately".

### BackendStatus & events

```csharp
BackendStatus.Of(DeviceRunState.Starting, "Waiting for boot", progress: 45);
BackendStatus.Of(DeviceRunState.Running, "Running", 100, serial: "emulator-5554");
```

Raise `StateChanged` **on every transition** (on a background thread - it is
fine, the Manager marshals to the UI). `BootProgress` 0-100 gives the device
card its animated state pill.

### BackendContext

```csharp
public sealed class BackendContext
{
    public string SharedRoot { get; init; }     // %USERPROFILE%\AndroLink
    public string DeviceDataDir(DeviceConfig d);    // <root>\userdata\<id>
    public string DeviceSdcardDir(DeviceConfig d);  // <root>\userdata\<id>\sdcard
    public string DeviceCatalogFile(DeviceConfig d);// ...\appcatalog.json
}
```

## 2. Minimal plugin, step by step

1. **Create a class library** targeting `net8.0`:

   ```
   dotnet new classlib -n MyBackend
   dotnet add MyBackend reference path\to\AndroLink.Core.csproj
   ```

2. **Implement the interface**:

   ```csharp
   public sealed class MyHyperVBackend : IRuntimeBackend
   {
       private BackendContext? _context;
       private readonly Dictionary<string, BackendStatus> _status = new();

       public string Id => "myhyperv";
       public string DisplayName => "Hyper-V Android (custom)";
       public DeviceCapabilities Capabilities { get; } = new()
       {
           SupportsInstall = true, SupportsShell = true,
           SupportsFileSync = true, LiveSharedFolder = false,
       };

       public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;
       public event Action<DeviceConfig, string>? LogEmitted;
       public void Initialize(BackendContext context) => _context = context;

       public async Task StartAsync(DeviceConfig device, CancellationToken ct = default)
       {
           SetStatus(device, BackendStatus.Of(DeviceRunState.Starting, "Creating VM"));
           // ... start your VM / container / whatever ...
           SetStatus(device, BackendStatus.Of(DeviceRunState.Running, "Running", 100));
       }
       // ... etc
   }
   ```

3. **Build and drop the DLL** into `%LOCALAPPDATA%\AndroLink\backends\`
   (per-user) or `<install>\backends\` (machine-wide). Requirements:

   - one or more public, concrete `IRuntimeBackend` implementations,
   - a **public parameterless constructor**,
   - reference only `AndroLink.Core` (+ your own dependencies, which you
     deploy next to your DLL).

4. **Create a device** in the Manager - your backend appears in the backend
   picker automatically. Pass configuration through
   `DeviceConfig.BackendOptions` (a string dictionary the user-facing UI and
   your backend agree on, e.g. `imagePath`, `serial`, `qemuPath`).

## 3. Composition: reusing the ADB bridge

If your engine exposes an adbd (almost all Android engines do), don't rewrite
install/shell/sync - **embed `AdbBackend`** exactly like the QEMU reference
plugin does:

```csharp
private readonly AdbBackend _adb = new();

public void Initialize(BackendContext context)
{
    _context = context;
    _adb.Initialize(context);
    _adb.StateChanged += (s, e) => StateChanged?.Invoke(s, e);
    _adb.LogEmitted  += (d, m) => LogEmitted?.Invoke(d, m);
}

public async Task StartAsync(DeviceConfig device, CancellationToken ct)
{
    // 1. boot YOUR engine
    LaunchYourVm(device);
    // 2. make it reachable through an adb server
    var client = new AdbClient();
    await AdbServer.EnsureServerRunningAsync(null, ct);
    await client.DeviceServiceAsync(null, "host:connect:127.0.0.1:5555", ct);
    // 3. delegate everything else
    await _adb.StartAsync(WithSerial(device, "127.0.0.1:5555"), ct);
}

public Task InstallAppAsync(DeviceConfig d, string apk, IProgress<InstallProgress>? p, CancellationToken ct)
    => _adb.InstallAppAsync(WithSerial(d, "127.0.0.1:5555"), apk, p, ct);
```

`AdbBackend` understands these `BackendOptions` keys: `adbPath`, `adbHost`,
`adbPort`, `serial`, `startCommand`, `stopCommand`, `bootTimeoutSec`.

## 4. The openGApps hook

`DeviceConfig.GAppsZipPath` holds a user-supplied openGApps zip. AndroLink
itself never ships Google apps. If you declare `SupportsGApps`, the expected
behavior (see `SimulatedBackend.ApplyGAppsMarker` for the minimal version):

1. validate with `GAppsPlan.FromDevice(device).Validate()` (null = OK),
2. on the device's **first boot**, sideload the zip into the recovery
   partition or flash it your engine's way,
3. record what you did (the simulated backend writes `gapps-applied.json`).

## 5. Checklist for a well-behaved plugin

- [ ] `StartAsync` raises `Starting` states with `BootProgress`, ends in `Running`
- [ ] failures end in `DeviceRunState.Error` with a useful `Detail`
- [ ] `StopAsync` is idempotent and safe when the device is already gone
- [ ] `GetStatusAsync` is cheap (the Manager polls it every few seconds)
- [ ] installs report `InstallProgress` (push %, installing, done)
- [ ] file operations respect `PathMapper` and never escape the device folder
- [ ] no UI dependencies - `AndroLink.Core` (+ Adb if you compose) only
- [ ] dispose cleanly (kill child processes, cancel boot loops)
