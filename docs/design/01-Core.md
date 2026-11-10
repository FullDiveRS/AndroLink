# Design: AndroLink.Core

The kernel assembly: everything device- and storage-shaped, no UI, no I/O to
Android itself.

## Modules

### Device model
- `DeviceConfig` - persistent definition (id, name, resolution, dpi, RAM, CPU,
  `BackendId`, optional `ImagePath`, `GAppsZipPath`, free-form
  `BackendOptions` dictionary). Serialized to `devices.json` via
  `DeviceRegistry` (atomic JSON writes, reload-safe).
- `DeviceRunState` - Stopped / Starting / Running / Error.

### Runtime contract (`Runtime/`)
- `IRuntimeBackend` + `BackendContext` + `DeviceCapabilities` +
  `BackendStatus` + `InstallProgress` - the plugin contract (see
  ../PLUGIN_AUTHORING.md).
- `BackendCatalog` - registry + plugin loader. Scans
  `<install>\backends` and `%LOCALAPPDATA%\AndroLink\backends`, loads each DLL
  in its own `AssemblyLoadContext`, instantiates concrete
  `IRuntimeBackend`s with parameterless ctors, reports per-file results via
  `PluginLoadReported`, raises `BackendAdded` so hosts can wire events.
- `DeviceManager` - the facade the Manager app talks to: start/stop/status,
  install (inspect → backend → catalog update), uninstall, list apps, delete
  device (with shared-folder cleanup), path mapping helpers, and the
  aggregated `DeviceStatusChanged` event.

### Storage (`FileSystem/`)
- `PathMapper` - the one true Android↔Windows mapping for `/sdcard`
  (mixed mode). Boundary-exact prefix matching, traversal rejection,
  both directions, unit-tested.
- `SharedFolderLayout` - creates the per-device tree (standard media dirs,
  `Android/AndroLink/apps`), writes `.androlink-device.json` and the
  user-facing `README.txt`.

### APK inspection (`Apk/`)
- `BinaryXmlParser` - Android binary XML (AXML): chunk walk (string pool,
  start/end element), UTF-16 and UTF-8 string pool decoding, typed values
  (STRING / INT_DEC / INT_HEX / REFERENCE / BOOLEAN).
- `ArscLabelResolver` - minimal `resources.arsc` reader that resolves one
  resource id (the manifest `@string/app_name` label) against the global value
  pool, package chunks, dense and sparse (Android 8+) type chunks. Best-effort:
  any parse failure returns null and the package name is used instead.
- `ApkInspector` - zip read + parse + `ApkInfo` (package, label, versions,
  min/target SDK, permissions). Never throws.

### Apps & services
- `InstalledAppCatalog` / `InstalledApp` - per-device JSON index of installed
  apps (package, label, version, APK file name, timestamps).
- `GAppsHook` / `GAppsPlan` - openGApps zip validation (exists, is zip, size
  sanity). Backends with `SupportsGApps` act on it.
- `Log` - thread-safe file + console + event logger; `Json` - serializer with
  camelCase, enums-as-strings, atomic writes; `AndroLinkPaths` - well-known
  locations.

## Invariants

1. Core references no UI and no backends.
2. All path translation goes through `PathMapper` - callers never string-concat
   user storage paths.
3. All persistence goes through `Json` (atomic `.tmp` + rename).
4. Inspection/parsing never throws; operations that talk to backends may throw
   and the Manager converts failures into dialogs.
