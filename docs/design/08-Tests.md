# Design: AndroLink.Tests

27 xUnit tests covering the protocol, storage, and layout foundations of
AndroLink. All of them run headlessly on Linux and Windows - no Android device
and no display required.

## Coverage map

| Test class | What it protects |
|---|---|
| `PathMapperTests` | The /sdcard ⇄ Windows mapping: recursive mapping, non-mapping of `/data`, `..` traversal rejection, exact prefix boundary (`/sdcardx`), Windows→Android round trip |
| `ConfigTests` | DeviceConfig JSON round trip, registry create/persist/delete, APK inspection of a synthetic APK (package, version, minSdk, label via resources.arsc, permissions), garbage-input safety |
| `AdbFramingTests` | Smart-socket wire format against a fake adb server on loopback: request framing, version response, device list parsing, FAIL surfacing, transport+sync handshake with a STAT round trip |
| `LayoutTests` | StackPanel accumulation + spacing, Grid star/pixel/auto resolution and placement, DockPanel slicing + fill, ScrollViewer clamping + NeedsScroll, Border padding, theme palette completeness |
| `SimulatedBackendTests` | Boot lifecycle (folders, sample files, state events), APK install into the live shared folder + catalog update, uninstall cleanup, GApps validation |

## Notable helpers

- `TestApkBuilder` - constructs a structurally valid APK in memory: a binary
  XML manifest (AXML chunks, UTF-16 string pool, typed attribute values) plus
  a minimal `resources.arsc` (table header → global value pool → package →
  typeStrings/keyStrings pools → typeSpec → dense type chunk with a STRING
  entry). This is what makes `BinaryXmlParser` + `ArscLabelResolver` +
  `ApkInspector` testable end-to-end without shipping a real APK.
- `FakeAdbServer` / `FakeSyncServer` - TcpListener-based adb server stand-ins
  that assert the exact bytes on the wire.

## Conventions
- Tests use temp folders with GUID names and clean up in `finally`.
- No sleeps except where the simulated backend's boot stages require awaiting
  real `Task.Delay`s (~3 s per boot test, acceptable).
- New wire-format or mapping behavior must come with a test here first.
