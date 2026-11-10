# AndroLink Architecture

This document describes how AndroLink is put together, what each component
owns, and how the two most important flows - **APK installation** and **file
access** - move through the system.

## 1. Bird's-eye view

```
+---------------------------------------------------------------------------------+
|                                  Windows desktop                                |
|                                                                                 |
|  +---------------------------+      +---------------------------------------+  |
|  |   AndroLink.Manager       |      |            AndroLink.UI              |  |
|  |   (WPF-free C# app)       |----->|  retained-mode control tree          |  |
|  |   Devices/Apps/Files/     |      |  measure/arrange layout engine       |  |
|  |   Android images/Settings |      |  vector icons, themes + animations   |  |
|  |   pages                   |      |  SkiaSharp renderer                  |  |
|  +---------------------------+      +------------------+--------------------+  |
|            |                                           |                        |
|  +---------v---------------------------+   +---------v--------------------+  |
|  |   AndroLink.Core                    |   |  AndroLink.Win32             |  |
|  |   DeviceManager (facade)            |   |  window host / message loop  |  |
|  |   DeviceRegistry + DeviceConfig     |   |  input translation, DPI      |  |
|  |   BackendCatalog (plugin loader)    |   |  DIB <--> SKSurface blitting |  |
|  |   PathMapper (Android<->Windows)    |   |  tray, dialogs, clipboard    |  |
|  |   ApkInspector (AXML + arsc)        |   +------------------------------+  |
|  |   SystemImageCatalog + Store        |                                    |
|  |   InstalledAppCatalog, GAppsHook    |                                    |
|  +---------+---------------------------+                                    |
|            |  IRuntimeBackend plugin contract                               |
|  +---------v----------------------------------------------------------------+ |
|  |                     BackendCatalog / backends                            | |
|  |  +--------------------+  +------------------+  +------------------+      | |
|  |  | SimulatedBackend   |  | AdbBackend       |  | QemuBackend      |      | |
|  |  | (built-in demo,    |  | (pure-C# ADB     |  | (reference:      |      | |
|  |  |  live sdcard)      |  |  client, mirror) |  |  Android-x86 VM, |      | |
|  |  +--------------------+  +------------------+  |  x64 + aarch64)  |      | |
|  |  +------------------------------------+        +------------------+      | |
|  |  | EmulatorBackend (SDK emulator:    |                                  | |
|  |  |  boots downloaded images,         |                                  | |
|  |  |  x86_64 + arm64-v8a)              |                                  | |
|  |  +------------------------------------+                                  | |
|  +--------------------------------------------------------------------------+ |
|                                                                                |
|  %USERPROFILE%\AndroLink\userdata\<deviceId>\sdcard   <--- THE shared storage  |
|      DCIM\  Download\  Documents\  Music\  Movies\  Pictures\  Android\...     |
+---------------------------------------------------------------------------------+
```

## 2. Process & project boundaries

One user-facing process (`AndroLink.Manager.exe`), eight assemblies:

| Assembly | Responsibility | References |
|---|---|---|
| `AndroLink.Core` | Everything device- and storage-shaped: config, registry, path mapping, APK parsing, the Android system-image catalog + download store, backend contract, plugin loader, `DeviceManager` facade | (none) |
| `AndroLink.Adb` | ADB wire protocol (smart-socket + sync), `pm install`, the ADB runtime backend, the Android-SDK emulator backend (SDK locator, AVD writer, launcher) | Core |
| `AndroLink.UI` | The custom UI framework: element tree, layout, controls, themes, animation, platform abstraction | SkiaSharp |
| `AndroLink.Win32` | Production `IPlatform`: windowing, input, drag-drop, tray, dialogs, clipboard | Core, UI |
| `AndroLink.Backend.Simulated` | Demo device: real folders, simulated boot/install | Core |
| `AndroLink.Backend.Qemu` | Reference VM plugin (composes AdbBackend) | Core, Adb |
| `AndroLink.Manager` | Application shell + pages + dialogs | all of the above |
| `AndroLink.Tests` | xUnit suite (40 tests) | Core, Adb, UI, Simulated |

The layering rule: **Core never references UI**, backends never reference UI,
UI never references Core. The Manager is the only assembly that sees all of
them, which keeps the framework reusable and the backends testable headlessly
(the whole test suite runs on Linux).

## 3. The plugin contract

Everything that "runs Android" implements `AndroLink.Core.IRuntimeBackend`:

```csharp
public interface IRuntimeBackend : IDisposable
{
    string Id { get; }                       // referenced by DeviceConfig.BackendId
    string DisplayName { get; }
    DeviceCapabilities Capabilities { get; } // install? shell? file sync? live folder? GApps?
    event EventHandler<DeviceStateChangedEventArgs> StateChanged;
    event Action<DeviceConfig, string> LogEmitted;
    void Initialize(BackendContext context); // shared root, per-device folders
    Task StartAsync(DeviceConfig device, ...);
    Task StopAsync(DeviceConfig device, ...);
    Task<BackendStatus> GetStatusAsync(DeviceConfig device, ...);
    Task InstallAppAsync(DeviceConfig device, string apkPath, IProgress<InstallProgress>?, ...);
    Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(...);
    Task UninstallAppAsync(...);
    Task SyncFromDeviceAsync(...) / PushToDeviceAsync(...);
    Task<string?> ResolveAdbSerialAsync(...);
}
```

- Built-ins are registered at startup (`RegisterBuiltInBackend`).
- Plugins are plain class libraries dropped into `backends\` (install dir or
  `%LOCALAPPDATA%\AndroLink\backends`); `BackendCatalog` scans, loads them in
  isolated `AssemblyLoadContext`s and instantiates every concrete
  `IRuntimeBackend` with a parameterless constructor.
- `DeviceCapabilities` drives the UI: a backend with `LiveSharedFolder` gets no
  sync buttons; one without `SupportsInstall` hides the install page actions.

See [PLUGIN_AUTHORING.md](PLUGIN_AUTHORING.md) for a full walkthrough.

## 4. Flow: APK installation

```
User picks / drops .apk
        |
        v
AppsPage.InstallInternalAsync
  |-- ApkInspector.Inspect(path)              (Core)
  |     |-- ZipFile -> AndroidManifest.xml
  |     |-- BinaryXmlParser (AXML chunks, string pools, typed values)
  |     '-- ArscLabelResolver (resources.arsc -> @string/app_name)
  |-- MessageBox confirm (name, package, version, size, device)
  |-- DeviceManager.InstallApkAsync(device, apk, progress)
  |     |-- RequireBackend(device).InstallAppAsync
  |     |     |-- SimulatedBackend: copy APK -> sdcard\Android\AndroLink\apps\<pkg>.apk
  |     |     |-- AdbBackend: ADB sync push -> /data/local/tmp, `pm install -r -t`, verify, rm
  |     |     '-- QemuBackend: launch QEMU + adb connect, then AdbBackend
  |     '-- InstalledAppCatalog.AddOrUpdate -> userdata\<id>\appcatalog.json
  '-- progress -> ProgressBar on Apps page
```

Design choices worth knowing:

- **Inspector never throws** - a broken APK yields `null` and a clean error
  dialog instead of a crash.
- The ADB route mirrors what `adb install` does (push + `pm install`) because
  that works on every adbd, old and new; exit status is captured with an
  `echo` marker so we don't depend on the newer `shell:v2` framing.
- The per-device `appcatalog.json` is the cache the Manager lists instantly;
  `AdbBackend.ListInstalledAppsAsync` reconciles it with live
  `pm list packages -3` output.

## 5. Flow: file access (the "editable outside the app" requirement)

```
   Android path                        Windows path
   /sdcard/DCIM/photo.jpg  <--.PathMapper-->  %USERPROFILE%\AndroLink\userdata\
                                              <deviceId>\sdcard\DCIM\photo.jpg
```

- `PathMapper.ToWindowsPath / ToAndroidPath` do one mapping and reject
  everything else (`..` traversal, `/sdcardx` prefix tricks, paths outside
  `/sdcard`).
- **Simulated backend**: `Capabilities.LiveSharedFolder = true` - the device's
  storage *is* the folder. Explorer edits show up on the device immediately.
- **ADB backend**: the folder is a *mirror*; `SyncFromDeviceAsync` /
  `PushToDeviceAsync` run the ADB sync protocol (LIST/RECV/SEND) recursively.
  The Files page only shows sync buttons when
  `SupportsFileSync && !LiveSharedFolder`.
- **QEMU backend** (roadmap): bind-mount the same folder into the VM via
  9p/virtfs so it becomes live like the simulated backend. See
  [FILE_ACCESS.md](FILE_ACCESS.md).

`SharedFolderLayout.EnsureDeviceFolders` creates the tree (standard Android
media dirs, `Android/AndroLink/apps`, a `.androlink-device.json` marker and a
plain-language `README.txt` inside the sdcard folder explaining the mapping to
whoever opens it in Explorer).

## 6. Flow: a rendered frame

```
Win32 message loop ( GetMessage / DispatchMessage )
  |-- WM_TIMER (15 ms)  -> Animator.Tick()        (tweens, caret blink, pulse)
  |-- WM_PAINT          -> Win32WindowHost.RenderIfDirty()
  |     |-- SKSurface.Create(Bgra8888 over CreateDIBSection memory)
  |     |-- Window.RenderFrame(canvas, dpiScale)
  |     |     |-- if layout dirty: MeasureInternal(client) + ArrangeInternal
  |     |     |-- UiRenderer.RenderTree(root)
  |     |           per element: translate -> opacity layer -> background ->
  |     |           OnRender (control chrome) -> border -> clip -> children
  |     '-- BitBlt DIB -> window DC            (no flicker: WM_ERASEBKGND -> 1)
  |-- input messages    -> Window.DispatchPointer*/Key/Text
  |     |-- hit-test: deepest visible element (overlay first)
  |     '-- bubble with element-local coordinates; Handled stops the chain
  '-- WM_APP_DISPATCH   -> UIApplication.Post queue (backend thread -> UI)
```

Device state polling runs on a `System.Threading.Timer` and marshals updates
to the UI thread through the same dispatch queue.

## 7. Persistence map

| Data | Location |
|---|---|
| Device definitions | `%LOCALAPPDATA%\AndroLink\devices.json` |
| Global settings | `%LOCALAPPDATA%\AndroLink\settings.json` |
| Logs | `%LOCALAPPDATA%\AndroLink\logs\androlink-manager-*.log` |
| **Device storage (/sdcard)** | `%USERPROFILE%\AndroLink\userdata\<deviceId>\sdcard\` |
| App catalog per device | `%USERPROFILE%\AndroLink\userdata\<deviceId>\appcatalog.json` |
| Device marker | `%USERPROFILE%\AndroLink\userdata\<deviceId>\.androlink-device.json` |
| Backend plugins | `<install>\backends\*.dll`, `%LOCALAPPDATA%\AndroLink\backends\*.dll` |

All JSON is written atomically (`.tmp` + rename) via `AndroLink.Core.Json`.

## 8. Design decisions & trade-offs

- **Custom UI framework instead of WPF/WinForms** - the user's requirement,
  and it keeps the whole stack self-contained: one rendering story (SkiaSharp),
  no XAML, no system theme surprises. Cost: reimplemented text input,
  scrollbars, dialogs (all intentionally small and visible in `AndroLink.UI`).
- **ADB via the smart-socket client protocol** (what `adb.exe` itself speaks
  to the server) instead of implementing the CNXN/AUTH peer protocol: zero
  crypto key management, works with every existing device/server, and the
  server binary is found/started by `AdbServer`. Direct adbd peering is a
  documented future step for VM backends that bundle adbd.
- **Mixed storage mode** (system image internal, `/sdcard` on real folders) is
  the product's defining feature - it trades absolute fidelity (no ext4
  permissions/symlinks on the sdcard) for the requirement that files stay
  ordinary Windows files.
- **JSON instead of a database** - small data, human-debuggable, atomic writes.
- **xUnit over loopback sockets** - the ADB tests run a fake server so the
  wire format is regression-protected without a device.
