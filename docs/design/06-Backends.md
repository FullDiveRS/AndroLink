# Design: Runtime backends

Two shipped backends beyond the ADB bridge, and what future engines should do
differently.

## SimulatedBackend (built-in, fully working)

Purpose: a complete, honest demo of the whole product without Android itself.

- **Boot**: staged progress (powering on → system image → zygote → apps →
  optimizing → running, ~3 s), fully cancellable; per-device
  `CancellationTokenSource` bookkeeping.
- **Storage**: `SharedFolderLayout.EnsureDeviceFolders` at boot (standard
  `/sdcard` dirs + README + marker). `LiveSharedFolder = true` - installs and
  file drops are plain file operations, visible live in Explorer.
- **Seeding**: first boot writes `Download/welcome.txt` and
  `Pictures/about-these-folders.txt` so the folder tells its own story.
- **Install**: copy APK → `sdcard\Android\AndroLink\apps\<package>.apk` with
  byte-progress, register in `appcatalog.json`, log. Uninstall reverses both.
- **GApps hook**: validates the user's openGApps zip, writes
  `gapps-applied.json`, logs the outcome.

## EmulatorBackend (built-in: downloaded images on the SDK emulator)

The Device-Manager path: boots the images downloaded on the Images page with
the **official Android SDK emulator** (the engine Android Studio itself uses),
so both x86_64 and arm64-v8a images run on the matching host.

- **Image resolution**: `DeviceConfig.ImagePackage` must exist in the
  `SystemImageStore`, otherwise Start fails with actionable text.
- **SDK discovery**: `sdkDir` option -> `ANDROID_HOME` -> `ANDROID_SDK_ROOT`
  -> `%LOCALAPPDATA%\Android\Sdk` -> Program Files (`AndroidSdkLocator`).
- **AVD**: `AvdWriter.CreateAvd` writes
  `%LOCALAPPDATA%\AndroLink\avd\androlink_<deviceId>.avd\config.ini`
  (`image.sysdir.1` pointing into the image store, LCD/RAM/cores from the
  device config). At launch the backend sets `ANDROID_SDK_ROOT` to the image
  store root and `ANDROID_AVD_HOME` to the AndroLink avd folder.
- **Launch**: `emulator -avd <name> -port 5554 -gpu auto -no-boot-anim`;
  if the process dies instantly (port busy) it relaunches without a fixed
  port. Boot wait is delegated to an embedded `AdbBackend` pinned to
  `emulator-<port>` (default timeout 240 s).
- **Stop**: `emu kill` via the ADB backend, then hard-kill fallback.

## QemuBackend (reference plugin, experimental)

Demonstrates the two patterns plugin authors need: **launching an external
engine** and **composing the ADB bridge**.

- Options: `qemuPath` (auto-resolved), `imagePath` (required Android-x86
  qcow2/iso), `extraArgs`, `bootTimeoutSec`.
- **Architecture**: the guest ABI (`DeviceConfig.Abi` or the last segment of
  `ImagePackage`) selects `qemu-system-x86_64` or `qemu-system-aarch64`;
  cross-architecture runs log a TCG slow-emulation warning. Accelerators are
  tried in order (`whpx` then `tcg` on x64 Windows, `tcg` only on ARM64
  Windows, `kvm` then `tcg` elsewhere).
- Command line: `-accel <acc> [-accel tcg] -m <RamMb> -smp <CpuCores>
  -drive file=<image>,format=qcow2,if=virtio
  -nic user,hostfwd=tcp:127.0.0.1:5555-:5555 -device virtio-tablet
  -display sdl -audio none` (plus `-M virt` for aarch64 guests).
- Boot: start QEMU → ensure adb server → `host:connect:127.0.0.1:5555` →
  delegate the rest to an embedded `AdbBackend` pinned to that serial.
- Stop: `reboot -p` → wait → hard-kill the process tree as fallback.
- GApps: logs guidance (the backend does not flash images).

## Roadmap: a "live folder" VM backend

The end-state storage model for real Android:

1. launch the VM with the device sdcard bind-mounted via 9p/virtfs
   (`-virtfs local,path=<sdcard>,mount_tag=sdcard,security_model=...`),
2. init script mounts `mount_tag=sdcard` at `/sdcard` (Android-x86 supports
   this via a custom init/rc edit or a recovery-booted overlay),
3. declare `LiveSharedFolder = true`, `SupportsFileSync = false`,
4. keep ADB for installs/shell.

Result: the Windows folder *is* the device's `/sdcard` for real Android -
exactly the simulated backend's behavior, without mirrors or sync.

## Other engine ideas

| Backend | Notes |
|---|---|
| Hyper-V | WSA-style: VM with virtio, adbd over hostfwd; requires Hyper-V feature detection |
| WSL2 | Android inside WSL2 distro, GUI via Weston/RDP; storage via /mnt/drvfs bind mount (live folder!) |
| Remote device | adb over Wi-Fi; the AdbBackend already supports `adbHost`/`adbPort`/`serial` options |
