# Design: AndroLink.Adb

A pure-C# Android Debug Bridge implementation plus the ADB runtime backend.
No adb.exe is needed at runtime except to *host the server* (same as any adb
client).

## Layers

### 1. `AdbConnection` - the smart socket
Every connection to the adb server (default `127.0.0.1:5037`) speaks:
```
client -> server:  4 hex digits (payload length) + UTF-8 payload
server -> client:  "OKAY" (then service data) or "FAIL" (4-hex-len + message)
```
Services used:
- `host:version` - handshake/health probe
- `host:devices` - serial + state lines (`adb devices` format)
- `host:transport-any` / `host:transport:<serial>` - bind the socket to a device
- `host:connect:<host:port>` - make the server connect to a remote adbd (used by the QEMU plugin)
- `shell:<command>` / `sync:` - device services after transport binding

`ReadExactlyAsync` guards partial reads; timeouts wrap the connect phase;
exit codes are captured by appending `; echo __AL_RC__$?` (works on every
adbd, no dependency on `shell:v2`).

### 2. `AdbClient` - the high-level client
`GetServerVersionAsync`, `ListDevicesAsync`, `DeviceServiceAsync` (raw service
stream), `RunShellAsync` (with exit-status verification), `OpenSyncAsync`.

### 3. `AdbSyncClient` - the sync protocol (v1)
Frame format: 4-byte ASCII id + u32 LE length (+ payload).
- `STAT path` -> `STAT` mode/size/mtime
- `LIST path` -> `DENT` entries -> `DONE`
- `SEND "path,0100644"` -> `DATA` chunks (32 KiB) -> `DONE mtime` -> `OKAY`
- `RECV path` -> `DATA` chunks -> `DONE`
- `FAIL` anywhere -> `AdbException`
Directory push/pull recurses via LIST + per-entry file ops and `mkdir -p`
through a separate shell connection (deliberately avoids the legacy
directory-SEND quirk).

### 4. `AdbInstaller`
`adb install` semantics: push APK to `/data/local/tmp/androlink_<guid>_<name>.apk`
-> `pm install -r -t` -> verify "Success" -> `rm -f` cleanup. Progress:
push bytes (5-40%) + pm phase (50-100%). Failure text is parsed from
`Failure [...]` output.

### 5. `AdbServer`
Locates adb.exe (configured path -> PATH -> `%LOCALAPPDATA%\Android\Sdk\platform-tools`
-> Program Files), probes port 5037, launches `adb start-server` and waits up
to 10 s.

### 6. `AdbBackend : IRuntimeBackend`
Bridge to *any* ADB-visible Android. Start sequence: ensure server → run
optional `startCommand` (e.g. user's QEMU script) → poll devices for the
pinned/auto serial → poll `getprop sys.boot_completed` → Running.
Options: `adbPath`, `adbHost`, `adbPort`, `serial`, `startCommand`,
`stopCommand`, `bootTimeoutSec`. Stop: `stopCommand` or `emu kill` for
`emulator-*` serials; external devices are never force-rebooted.
Install/uninstall/list/sync delegate to the layers above; the app catalog is
reconciled with `pm list packages -3` + known labels.

## Testing
`AdbFramingTests` runs a fake adb server on the loopback interface and asserts
the wire format end-to-end: request framing, version response parsing, device
list parsing, FAIL surfacing, and the transport+sync handshake with a STAT
round-trip. No device required.
