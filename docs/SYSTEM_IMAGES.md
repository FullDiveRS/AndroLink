# Android images: downloading Android versions (x64 + ARM)

The **Android images** page in the AndroLink Manager is the equivalent of
Android Studio's *Device Manager / SDK Manager* system-image flow: browse the
Android versions Google publishes, download the ones you want, and attach them
to devices.

---

## 1. What you can download

Every entry is one **system image** = one *Android version* + one *build tag* +
one *CPU architecture*:

| Dimension | Values |
|---|---|
| Android version | API 24 (Android 7.0) up to API 36 (Android 16) — whatever Google's repository currently lists on the stable channel |
| Build tag | **Google APIs** (`google_apis`), **Google Play** (`google_apis_playstore`), **AOSP/default** (`android`/`default`), Android TV, Android Wear, Automotive |
| CPU/ABI | `x86_64`, `arm64-v8a`, `x86`, `armeabi-v7a` |

The catalog is fetched live from Google's public per-tag repositories:

```
https://dl.google.com/android/repository/sys-img/<tag>/sys-img2-3.xml
```

(the same XML files the Android Studio SDK Manager reads). Each package entry
carries the zip URL, byte size and SHA-1, which AndroLink verifies after
downloading.

**Offline fallback.** A snapshot of the stable catalog (newest revision per
API/tag/ABI, generated from the real repository data) is compiled into
`AndroLink.Core` (`SystemImages/KnownImages.g.cs`), so the page works with no
network. Press **Refresh catalog** to go back online; the status text shows
which source is active.

## 2. Where images are installed

```
%LOCALAPPDATA%\AndroLink\system-images\
  system-images\android-34\google_apis\x86_64\
    system.img
    kernel-ranchu
    ramdisk.img
    ...
    androlink-image.json        <- AndroLink marker (metadata, install date)
```

The layout intentionally mirrors an Android SDK folder. That lets the emulator
backend point `ANDROID_SDK_ROOT` at this root and boot the image with
`image.sysdir.1=system-images/android-34/google_apis/x86_64/`, exactly like a
normal AVD. "Open images folder" on the Images page reveals the root in
Explorer; installed images can be deleted from the same page (they are
re-downloadable any time).

## 3. x64 and ARM support

AndroLink detects the host CPU (`System.Runtime.InteropServices.RuntimeInformation`)
and says which ABI runs natively:

| Host | Native ABI | Note |
|---|---|---|
| x64 PC (Intel/AMD) | `x86_64` (and `x86`) | recommended + marked green in the image list |
| ARM64 device (Snapdragon, Windows-on-ARM) | `arm64-v8a` (and `armeabi-v7a`) | arm64 system images run natively; x86_64 images would need full CPU emulation |

Rules of thumb baked into the UI:

- The **Create device** dialog preselects the ABI that matches your CPU and
  shows an explainer banner.
- Image rows whose ABI runs natively get a green ABI chip and a
  **recommended** badge.
- Cross-architecture is never blocked, but backends log a warning (QEMU under
  TCG, or the emulator's own translation layer) — expect it to be slow.

### Backend support matrix

| Backend (`IRuntimeBackend`) | What it runs | x64 host | ARM64 host | /sdcard model |
|---|---|---|---|---|
| `simulated` (built-in) | No real Android; the boot sequence and app catalog reflect the picked image — everything else (shared folders, APK installs, GApps hook) is real | ✓ | ✓ | **Live** Windows folder |
| `emulator` (built-in) | Downloaded system images via the **Android SDK emulator** — the same engine Android Studio's Device Manager uses; needs `emulator` + `platform-tools` from an Android SDK | x86_64 images ✓, arm64 images via emulator translation | arm64-v8a images ✓ (native), x86_64 via translation | ADB-sync mirror |
| `adb` (built-in) | Any Android already visible over ADB (your own emulator instance, USB device, custom VM) | ✓ | ✓ | ADB-sync mirror |
| `qemu` (plugin example) | Android-x86 disk images/ISOs you supply; picks `qemu-system-x86_64` or `qemu-system-aarch64` based on the device ABI | ✓ | ✓ (aarch64 builds) | ADB-sync mirror |

> Downloaded Google/AOSP emulator images target the emulator's *goldfish*
> device model, which vanilla QEMU does not implement — use the **emulator**
> backend for those. QEMU remains the path for Android-x86 style bootable
> images. A 9p/virtfs bind mount (making `/sdcard` live like the simulated
> backend) is on the roadmap — see [FILE_ACCESS.md](FILE_ACCESS.md).

## 4. How a device uses an image

1. **Android images → Download** an image (progress bar, cancellable).
2. Optional but recommended: on the same page, **Installed on this PC →
   "Set default"** marks one downloaded image as your preferred Android
   version. The choice is highlighted with an accent border and a
   *Default image* pill, persisted to `settings.json`
   (`DefaultImagePackage`), shown in the status bar, and preselected in the
   Create device dialog. The first downloaded image becomes the default
   automatically; deleting the default image clears the choice.
3. **Devices → Create device** → pick the image under *Android version* and
   the CPU/ABI (your default image is preselected). The dialog warns if a
   backend was chosen that cannot run the selection (e.g. emulator backend
   with no image).
4. **Start**. The device card and boot progress show the Android version and
   ABI, e.g. `Android 14 (API 34) · arm64-v8a`.
5. Apps and files work exactly as documented in
   [FILE_ACCESS.md](FILE_ACCESS.md) - the storage model does not change with
   the backend.

`DeviceConfig` persists the choice as three fields: `ApiLevel`, `Abi` and
`ImagePackage` (the SDK package path, e.g.
`system-images;android-34;google_apis;x86_64`).

## 5. Developer notes

- Catalog parsing: `SystemImageCatalog.ParseRepository(Stream, tag)` is
  namespace-agnostic and covered by unit tests with a trimmed copy of the real
  schema (`tests: SystemImageTests.Repository_xml_parses_the_real_schema`).
- Download store: `SystemImageStore` streams the zip to
  `%LOCALAPPDATA%\AndroLink\system-images\.staging\*.zip.part`, hashes SHA-1
  while streaming, verifies against the repository checksum, extracts the
  `system-images/android-<api>/<tag>/<abi>/` prefix and writes the
  `androlink-image.json` marker. Progress events are raised on background
  threads; the UI marshals them via `AppServices.Post`.
- AVD creation for the emulator backend: `AvdWriter.CreateAvd(...)` writes
  `%LOCALAPPDATA%\AndroLink\avd\androlink_<deviceId>.avd\config.ini` +
  the matching `.ini`; `AndroidSdkLocator` finds the SDK through
  `sdkDir` option → `ANDROID_HOME` → `ANDROID_SDK_ROOT` →
  `%LOCALAPPDATA%\Android\Sdk` → Program Files fallbacks.
- Regenerating the offline snapshot (maintainers, with network access):

  ```bash
  for t in android google_apis google_apis_playstore android-tv android-wear android-automotive-playstore; do
    curl -sSL "https://dl.google.com/android/repository/sys-img/$t/sys-img2-3.xml" -o "/tmp/sysimg/$t.xml"
  done
  python3 scripts/make_known_images.py /tmp/sysimg src/AndroLink.Core/SystemImages/KnownImages.g.cs
  ```
