# Design: AndroLink.Manager (the application)

The desktop app tying everything together, built 100% on AndroLink.UI + the
Win32 host.

## Composition root

- `Program.Main` ([STAThread]): `AppServices.Initialize()` → build window →
  `StartPolling()` → `window.Show()` → `Platform.Run(window)`.
- `AppServices`: loads settings, creates `Win32Platform` + `DeviceManager`,
  registers built-in backends (Simulated, Adb), loads plugins, wires
  `DeviceStatusChanged`, and runs the status poller
  (`System.Threading.Timer`, `DevicePollIntervalMs`, results marshalled via
  `UIApplication.Post`). A native message-box fallback exists for
  pre-UI failures.

## Main window shell (`MainWindow.cs`)

```
DockPanel
├─ Dock.Left    NavRail (208px): logo, Devices / Apps / Files / Settings
├─ Dock.Bottom  status bar: device count, shared root, "files stay editable"
└─ Fill         Border hosting the active page
```

Also wires: APK drag-drop (any `.apk` dropped on the window jumps to Apps and
starts the install flow), the tray icon (open/quit), and theme selection from
settings.

## Pages

### DevicesPage
Persistent `DeviceCard` per device (vector icon badge, name, meta line with
the picked Android version/ABI, state pill, iconized Start/Stop buttons,
selection). Toolbar: Create / Start / Stop / Delete - all with vector icons.
Content sits in a centered, width-capped column (MaxWidth) so wide windows
stay balanced. `CreateDeviceDialog` (overlay modal): name, resolution presets
(720p / 1080p / tablet / phone), **Android version picker (ListBox of the
images downloaded on the Images page)**, **CPU/ABI chips with the
host-recommended one preselected**, RAM slider (512-8192, snapped to 256),
CPU slider (1-8), backend picker (from `BackendCatalog`), openGApps zip hook,
inline validation (e.g. emulator/qemu backends require an image or disk).

### ImagesPage (Android-version manager, Device-Manager style)
Two sections in one scroll: **Installed on this PC** (marker-driven, with
Open folder / Delete) and **Available to download** grouped by Android
version, one row per (tag, ABI) with size, native-ABI chips, "recommended"
badge and Download buttons. An info banner explains which ABI this PC runs
natively. Download state machine per package (`_downloading` set): the row
re-renders as progress bar + live bytes + Cancel; failures surface a message
box and restore the row. Catalog comes from `AppServices.ImageCatalog`
(built-in snapshot, upgraded to the live Google repository by
**Refresh catalog**). All store events are marshalled to the UI thread via
`AppServices.Post`.

### AppsPage
Device selector row (primary-highlighted buttons). ListView of installed apps
(App / Package / Version / Installed) refreshed from the backend async.
Install flow: pick or drop `.apk` → `ApkInspector` preview dialog → progress
bar with staged percentage → catalog refresh. Uninstall + "Open apps folder"
(`sdcard\Android\AndroLink\apps`) actions.

### FilesPage
The user-facing proof of the storage model. Device selector, breadcrumb
labels (Android path ⇄ Windows folder), ListView (Name/Type/Size/Modified)
browsing the sdcard tree directly, Up / Open in Explorer / Open shared root,
and - for mirror-mode backends - Sync from device / Push to device with status
text. Rows activate on double click to descend into folders.

### SettingsPage
Grid rows: shared storage root (+ browse), adb.exe path (+ browse), poll
interval, light-theme toggle, minimize-to-tray toggle. Save persists settings,
applies the theme, updates the tray behavior. Diagnostics: open logs folder,
open data folder, About (lists loaded backends).

## Threading model
- UI thread: message loop, layout, render, all element mutation.
- Backend threads: `Start/Stop/Install/Sync` are `async` fire-and-forget with
  try/catch → dialogs; status events and poller results marshal through
  `UIApplication.Post`; Apps/Files page refreshes guard against device
  switches (`ReferenceEquals` checks before applying async results).
