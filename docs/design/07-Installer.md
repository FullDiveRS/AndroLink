# Design: Installer (AndroLinkSetup.iss)

The installer is a **separate application**: an Inno Setup 6 script compiled
by `ISCC.exe` into `AndroLinkSetup.exe` (built by `scripts\build.ps1 -Installer`).

## Packages

| Component | Source | Destination |
|---|---|---|
| Manager app + all assemblies | `out\publish\*` (from `build.ps1 -Publish`) | `{autopf}\AndroLink` |
| Backend plugin folder | included in publish (`backends\`) | `{app}\backends` |
| Docs wiki | `docs\*` | `{app}\docs` |
| Shortcuts | Start menu + optional desktop icon | user's choice |

## Behavior

- **.NET 8 Desktop Runtime check** (`InitializeSetup`): registry probe of the
  dotnet setup keys (x64 / arm64 / x86). Missing runtime → offer to open the
  official download page; setup continues either way.
- **PATH task** (optional): appends `{app}` to the user `Path`
  (`HKCU\Environment`), only when not already present
  (`NeedsPathEntry` check). Inno removes it on uninstall.
- **Ready-memo customization**: explicitly tells the user that device files
  live outside the install folder and uninstalling never deletes them.
- **Uninstall**: removes program files and shortcuts only.
  `%USERPROFILE%\AndroLink\userdata` (device storage) and
  `%LOCALAPPDATA%\AndroLink` (settings/logs/devices.json) are intentionally
  preserved - `[UninstallDelete]` is deliberately empty.
- Per-user data roots are created on first app run, not by the installer
  (installers may run elevated; the data belongs to the user).

## Build

```powershell
.\scripts\build.ps1 -Publish -Installer
# -> installer\Output\AndroLinkSetup.exe
```

`AppId` is a fixed GUID so upgrades replace in place. Version comes from
`#define MyAppVersion` (kept in sync with `Directory.Build.props` `Version`).
