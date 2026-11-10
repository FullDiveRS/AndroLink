## This project was made by GLM-5.3-flash. you may study, use or fork it as long as it retains the MIT license

# AndroLink

**An Android Subsystem for Windows, written in C#.**

AndroLink runs emulated Android devices on Windows, installs APKs, and keeps every
device's `/sdcard` storage as **plain Windows folders that stay fully readable and
editable outside the app** - in Explorer, VS Code, backup tools, anything - even
while the device is running.

```
+----------------------------------------------------------------------------------+
|                                     AndroLink Manager                            |
|   Devices | Apps | Files | Android images | Settings    (AndroLink.UI + Win32)   |
+----------------------+---------------------------------------------+-------------+
                       |                                             |
         AndroLink.Core (device model, APK inspector,        AndroLink.UI
         system-image catalog + downloads, path mapping,     (custom C#
         plugin catalog)                                     framework)
                       |                                              |
    +------------------+--------------------+-------------------------+
    |                  |                    |                         |
SimulatedBackend  EmulatorBackend     AdbBackend                QemuBackend (plugin)
/ sdcard IS the   boots downloaded    bridges to any            Android-x86 in QEMU,
Windows folder    Android versions    Android over ADB          x86_64 + aarch64
(live storage)    via the SDK         (emulator, USB, VM)       binaries, bridges
                  emulator                                      over ADB
```

## Highlights

| Requirement | How AndroLink delivers it |
|---|---|
| Android apps installable | Drag-drop or pick an `.apk`; AndroLink parses the binary manifest, shows name/version/permissions, installs via the device's package manager and tracks it in a per-device app catalog |
| Files editable outside the app | Device `/sdcard` = real Windows folder (`%USERPROFILE%\AndroLink\userdata\<deviceId>\sdcard`); the Files page browses it directly and can open it in Explorer |
| Custom UI framework in C# | `AndroLink.UI` - a retained-mode framework with a stack/grid/dock layout engine, 15+ controls, a font-independent vector icon set, SkiaSharp rendering, themes and animations. The Manager app itself is built on it |
| Download Android versions | The **Android images** page is a Device-Manager-style downloader: pick an API level (Android 7 up to 16), a build (Google APIs / Google Play / AOSP / TV / Wear / Automotive) and a CPU architecture, watch live progress, cancel any time. Images install to `%LOCALAPPDATA%\AndroLink\system-images` in Android-SDK layout |
| x64 and ARM | Every image ABI is offered - `x86_64`, `arm64-v8a`, `x86`, `armeabi-v7a` - with the native one recommended for your PC (x64 PCs get x86_64, ARM64 / Windows-on-ARM devices get arm64-v8a). The emulator backend runs both; the QEMU plugin picks `qemu-system-x86_64` or `qemu-system-aarch64` |
| Installer as a separate application | `installer/AndroLinkSetup.iss` builds `AndroLinkSetup.exe` (Inno Setup): EULA, .NET 8 runtime check, PATH task, desktop icon, and it **never deletes your emulated device data on uninstall** |
| Pluggable runtime | `IRuntimeBackend` plugin contract; ships a working simulated device, an Android-emulator backend, an ADB device bridge, and a QEMU reference plugin. New engines (Hyper-V, WSL2, ...) drop into a `backends` folder |

## Solution layout

```
AndroLink.sln
src/
  AndroLink.Core              device model, config, APK inspection, path mapping,
                              Android system-image catalog + download store,
                              plugin catalog, DeviceManager facade
  AndroLink.Adb               pure-C# ADB client (smart-socket + sync protocols),
                              pm install, ADB runtime backend, Android SDK
                              emulator backend (AVD writer + launcher)
  AndroLink.UI                the custom UI framework (SkiaSharp-rendered,
                              vector icons)
  AndroLink.Win32             Win32 window host: message loop, input, drag-drop,
                              clipboard, tray, file dialogs
  AndroLink.Backend.Simulated fully working demo device with live shared folders
  AndroLink.Backend.Qemu      reference plugin: Android-x86 in QEMU (x86_64 and
                              aarch64) over ADB
  AndroLink.Manager           the desktop application (built on AndroLink.UI)
  AndroLink.Tests             xUnit tests (protocol, layout, mapping, system
                              images, backend)
installer/AndroLinkSetup.iss  Inno Setup package definition
scripts/build.ps1             build + test + publish (win-x64 / win-arm64) + installer
docs/                         full documentation wiki (see below)
```

## Quick start (developers)

```powershell
# Windows, with the .NET 8 SDK:
git clone <repo> AndroLink
cd AndroLink
.\scripts\build.ps1              # build + run tests
.\scripts\build.ps1 -Publish     # + publish win-x64 to out\publish
.\scripts\build.ps1 -Publish -Installer   # + AndroLinkSetup.exe

dotnet run --project src\AndroLink.Manager
```

On first launch: **Devices → Create device** (the *Simulated device (demo)* backend
needs nothing else), **Start**, then drag an `.apk` onto the window or use
**Apps → Install APK…**. Your device storage is at
`%USERPROFILE%\AndroLink\userdata\<deviceId>\sdcard` - open it in Explorer and
edit while the device runs.

To run a **real Android version**: open **Android images**, download e.g.
*Android 14 · Google APIs · x86_64* (~1.5 GB), then create a device with that
image and the *Android emulator (SDK images)* backend - it boots through the
Android SDK's emulator exactly like Android Studio's Device Manager (the SDK's
`emulator` + `platform-tools` packages must be installed, e.g. with Android
Studio). The *Simulated* backend "boots" any downloaded image without an SDK,
for demos and development.

## Documentation wiki

| Document | Contents |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | System overview, component responsibilities, data flows |
| [docs/BUILDING.md](docs/BUILDING.md) | Prerequisites, build/publish/install, troubleshooting |
| [docs/SYSTEM_IMAGES.md](docs/SYSTEM_IMAGES.md) | Downloading Android versions, ABIs (x64/ARM), where images live, backend support matrix |
| [docs/FILE_ACCESS.md](docs/FILE_ACCESS.md) | The shared-folder storage model and path mapping rules |
| [docs/PLUGIN_AUTHORING.md](docs/PLUGIN_AUTHORING.md) | Writing a runtime backend plugin, step by step |
| [docs/design/](docs/design/) | Per-module design docs (Core, Adb, UI, Win32, Manager, Backends, Installer) |

## Status & honest limitations

- The **simulated backend is fully functional today** (boot, installs, live
  shared folders) and now reflects the Android version/ABI you pick for a
  device. It does not run real Android code.
- The **Android-emulator backend** boots the downloaded system images with the
  official SDK emulator (the same engine Android Studio's Device Manager
  uses). It needs the Android SDK's `emulator` + `platform-tools` packages;
  `/sdcard` is mirrored over the ADB sync protocol.
- The **ADB backend** bridges to any Android visible over ADB (official
  emulator, USB device, custom VM).
- The **QEMU backend** is an experimental reference plugin: it launches an
  Android-x86 image if you supply one (x86_64 on x64 PCs, aarch64 builds on
  ARM devices) and delegates device operations to the ADB bridge. Downloaded
  Google/AOSP emulator images need the emulator backend instead - they target
  the emulator's goldfish device model, which vanilla QEMU lacks. A 9p/virtfs
  bind mount (making `/sdcard` live like the simulated backend) is the
  documented roadmap.
- AndroLink never bundles Google apps or Android system images: system images
  are downloaded by you from Google's public repository, and the openGApps
  hook stays bring-your-own-zip; see the notices in [LICENSE](LICENSE).

## License

MIT - see [LICENSE](LICENSE).
