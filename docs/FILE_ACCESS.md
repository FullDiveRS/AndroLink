# File Access: the shared-storage model

AndroLink's defining promise: **the emulated device's files are ordinary
Windows files, readable and editable outside the app, even while the device
runs.** This document specifies exactly how that works.

## 1. Storage tiers

AndroLink uses **mixed storage**:

| Tier | Location | Editable outside? |
|---|---|---|
| System image (/, /system, /data private) | backend-internal (demo: n/a; VM: disk image) | no - by design, and read-only while running |
| **User storage (`/sdcard`)** | **plain Windows folders** | **yes - always** |

## 2. The mapping

```
<SharedRoot>                              default: %USERPROFILE%\AndroLink
└─ userdata\
   └─ dev-xxxxxxxxxxxx\                   one folder per device (deviceId)
      ├─ sdcard\                          <-- the device's /sdcard
      │  ├─ DCIM\      (camera)
      │  ├─ Download\
      │  ├─ Documents\
      │  ├─ Music\
      │  ├─ Movies\
      │  ├─ Pictures\
      │  ├─ Android\
      │  │  ├─ data\      (app-private dirs as plain folders)
      │  │  ├─ obb\
      │  │  └─ AndroLink\apps\            installed APK copies
      │  ├─ README.txt                    explains the mapping, in plain words
      │  └─ .androlink-device.json        machine marker (device id, backend)
      ├─ appcatalog.json                  installed apps index
      └─ logs\
```

`AndroLink.Core.PathMapper` is the single source of truth:

```csharp
PathMapper.ToWindowsPath(device, sharedRoot, "/sdcard/DCIM/photo.jpg")
// -> %USERPROFILE%\AndroLink\userdata\dev-xxxxxxxx\sdcard\DCIM\photo.jpg

PathMapper.ToAndroidPath(device, sharedRoot, "...\\sdcard\\Download\\x.txt")
// -> "/sdcard/Download/x.txt"
```

Rules enforced by the mapper (all covered by unit tests):

- only `/sdcard` is mapped; everything else returns `null`;
- `..` traversal, absolute escapes and invalid characters are rejected;
- the boundary is exact: `/sdcardx` is *not* under `/sdcard`;
- separators are normalized in both directions.

## 3. Backend integration levels

| Backend | `LiveSharedFolder` | What edits mean |
|---|---|---|
| Simulated | **true** | The device reads/writes the folder directly. Explorer edits are instantly visible on the device; device writes appear in Explorer. No sync anywhere. |
| ADB | false | The folder is a **mirror**. Files → *Sync from device* pulls via the ADB sync protocol (recursive LIST/RECV); *Push to device* sends edited files back (recursive mkdir/SEND). |
| QEMU (reference) | false (today) | Same mirror behavior as ADB. |
| QEMU (roadmap) | true (target) | Bind-mount the same folder into the VM over 9p/virtfs (`-virtfs local,path=<sdcard>,mount_tag=sdcard` + init script mounting it at `/sdcard`). Then the folder becomes live storage exactly like the simulated backend, but backed by real Android. |

The UI adapts automatically from `DeviceCapabilities`: sync buttons appear
only when `SupportsFileSync && !LiveSharedFolder`.

## 4. Safety rules

- The mapper never escapes the device folder, so "delete /sdcard" from Android
  can never touch files above `userdata\<deviceId>\`.
- Deleting a device through the Manager deletes its `userdata\<deviceId>`
  folder (with a confirmation dialog) - nothing else.
- Uninstalling AndroLink never deletes `userdata` or `%LOCALAPPDATA%`
  (the Inno Setup script documents this in the ready-memo).
- `README.txt` inside every sdcard folder tells whoever opens it in Explorer
  what the folder is and where the mapping table lives.
- Folder names inside `sdcard` are case-sensitive to Android; the README and
  the marker JSON warn about renaming them.
