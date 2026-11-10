# Building AndroLink

## Prerequisites

| Tool | Needed for | Notes |
|---|---|---|
| .NET 8 SDK | everything | <https://dotnet.microsoft.com/download/dotnet/8.0> |
| Windows 10+ (x64/arm64) | running the Manager | The solution also *compiles* on Linux (CI-friendly) thanks to `EnableWindowsTargeting`, but the GUI needs Windows |
| Visual Studio 2022 (optional) | IDE | Any edition with the .NET desktop workload; Rider works too |
| Inno Setup 6 | the installer | <https://jrsoftware.org/isinfo.php> - only needed for `AndroLinkSetup.exe` |
| adb.exe (optional) | ADB backend | Auto-detected from PATH or the Android SDK; or set the path in Settings |
| QEMU + an Android-x86 image (optional) | QEMU backend | User-supplied; see docs/PLUGIN_AUTHORING.md |

## Build, test, run

```powershell
# Build + run all tests
.\scripts\build.ps1

# Equivalent manual commands
dotnet build AndroLink.sln -c Release
dotnet test src\AndroLink.Tests\AndroLink.Tests.csproj -c Release

# Run the Manager from source
dotnet run --project src\AndroLink.Manager
```

Linux/macOS (compile + tests only):

```bash
./scripts/build.sh
```

## Publish and package

```powershell
# Framework-dependent publish (requires .NET 8 Desktop Runtime on the target PC)
.\scripts\build.ps1 -Publish

# Self-contained variant (bigger, no runtime needed):
dotnet publish src\AndroLink.Manager\AndroLink.Manager.csproj `
    -c Release -r win-x64 --self-contained true -o out\publish

# Build AndroLinkSetup.exe (Inno Setup 6)
.\scripts\build.ps1 -Publish -Installer
# -> installer\Output\AndroLinkSetup.exe
```

The installer:

1. checks for the .NET 8 Desktop Runtime and offers the download page,
2. installs to `%ProgramFiles%\AndroLink` (with a `backends\` plugin folder),
3. creates Start-menu / optional desktop shortcuts,
4. can add AndroLink to the user PATH,
5. **never deletes your emulated device data** on uninstall - the shared
   storage under `%USERPROFILE%\AndroLink` and `%LOCALAPPDATA%\AndroLink`
   survive removal.

## First run checklist

1. `dotnet run --project src\AndroLink.Manager`
2. Devices → **Create device** → name it, keep *Simulated device (demo)*.
3. **Start** the device. The state pill goes `Starting 20% → 90% → Running`.
4. Open `%USERPROFILE%\AndroLink\userdata\<deviceId>\sdcard` in Explorer -
   note the `README.txt` and sample files the device seeded.
5. Apps → **Install APK…** (or drag an .apk onto the window) and confirm.
6. Files → browse the same folders inside AndroLink, or **Open in Explorer**.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `dotnet build` fails with `EnableWindowsTargeting` errors | You are building on an old SDK; use .NET SDK 8.0.100+ |
| Manager starts but window is blank | A GPU/driver issue is unlikely (software rendering), but check `%LOCALAPPDATA%\AndroLink\logs` for exceptions |
| "No adb server is running and no adb executable was found" | Install Android platform-tools or set `adb.exe path` in Settings |
| ADB device never appears | For the official emulator keep it running *before* starting the ADB device; for physical devices enable USB debugging and accept the RSA prompt |
| QEMU backend: "needs an Android-x86 image" | Set `imagePath` in the device's backend options (create-device dialog stores it per device) |
| Installer: "requires the .NET 8 Desktop Runtime" | Install from <https://dotnet.microsoft.com/download/dotnet/8.0/runtime> (Desktop runtime, x64) |
| Tests fail on Linux with `libSkiaSharp` errors | Restore the `SkiaSharp.NativeAssets.Linux.NoDependencies` package (already referenced by the test project) |

## Project conventions

- `Directory.Build.props` pins: C# 12, nullable enable, `EnableWindowsTargeting`.
- All persisted documents go through `AndroLink.Core.Json` (atomic writes).
- User-visible strings live inline (English); no resource pipeline yet.
- Commit style: conventional commits (`feat(ui): caret blink`).
