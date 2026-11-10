using System.Text;
using AndroLink.Core;
using AndroLink.Core.SystemImages;

namespace AndroLink.Adb;

/// <summary>
/// Creates AVD definitions for the official Android emulator, entirely inside
/// AndroLink's data folder (%LOCALAPPDATA%\AndroLink\avd). The AVD points at
/// a system image installed by the <see cref="SystemImageStore"/>; at launch
/// time ANDROID_SDK_ROOT is set to the image store root so
/// image.sysdir.1 resolves exactly like in a normal SDK.
/// </summary>
public static class AvdWriter
{
    /// <summary>AVD name for a device, e.g. "androlink_dev-1a2b3c4d5e6f".</summary>
    public static string AvdNameFor(DeviceConfig device)
    {
        var id = new string(device.Id.Where(char.IsLetterOrDigit).ToArray());
        return $"androlink_{id}";
    }

    /// <summary>
    /// Writes the AVD config for a device/image pair and returns the AVD name.
    /// </summary>
    /// <param name="avdHome">AVD home (AndroLinkPaths.AvdHome).</param>
    /// <param name="device">Device definition (resolution, RAM, cores).</param>
    /// <param name="image">The installed system image to boot.</param>
    /// <param name="storeRoot">SystemImageStore root (becomes image.sysdir.1 base).</param>
    public static string CreateAvd(string avdHome, DeviceConfig device, SystemImageInfo image, string storeRoot)
    {
        var name = AvdNameFor(device);
        var avdDir = Path.Combine(avdHome, $"{name}.avd");
        Directory.CreateDirectory(avdDir);

        var sysDir = $"system-images/android-{image.ApiLevel}/{image.TagId}/{image.Abi}/"
            .Replace('\\', '/');

        var config = new StringBuilder()
            .AppendLine("avd.ini.encoding=UTF-8")
            .AppendLine($"image.sysdir.1={sysDir}")
            .AppendLine($"tag.id={image.TagId}")
            .AppendLine($"tag.display={image.TagDisplay}")
            .AppendLine($"abi.type={image.Abi}")
            .AppendLine($"hw.lcd.width={device.Width}")
            .AppendLine($"hw.lcd.height={device.Height}")
            .AppendLine($"hw.lcd.density={device.Dpi}")
            .AppendLine($"hw.ramSize={device.RamMb}")
            .AppendLine($"hw.cpu.ncore={Math.Max(1, device.CpuCores)}")
            .AppendLine("hw.gpu.enabled=yes")
            .AppendLine("hw.gpu.mode=auto")
            .AppendLine("hw.keyboard=yes")
            .AppendLine("disk.dataPartition.size=4G")
            .AppendLine("cachePartition.size=512M")
            .AppendLine("fastboot.chosenSnapshotFile=")
            .AppendLine("fastboot.forceFastBoot=no")
            .AppendLine("hw.device.name=AndroLink")
            .AppendLine("image.androidVersion.codename=")
            .ToString();

        File.WriteAllText(Path.Combine(avdDir, "config.ini"), config, new UTF8Encoding(false));

        var ini = new StringBuilder()
            .AppendLine("avd.ini.encoding=UTF-8")
            .AppendLine($"path={avdDir}")
            .AppendLine($"path.rel=avd/{name}.avd")
            .AppendLine($"target=android-{image.ApiLevel}")
            .ToString();

        File.WriteAllText(Path.Combine(avdHome, $"{name}.ini"), ini, new UTF8Encoding(false));
        return name;
    }

    /// <summary>Deletes the AVD files for a device (best effort).</summary>
    public static void DeleteAvd(string avdHome, DeviceConfig device)
    {
        var name = AvdNameFor(device);
        try
        {
            var dir = Path.Combine(avdHome, $"{name}.avd");
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            var ini = Path.Combine(avdHome, $"{name}.ini");
            if (File.Exists(ini)) File.Delete(ini);
        }
        catch (Exception ex)
        {
            Log.Warn("avd", $"could not delete AVD {name}: {ex.Message}");
        }
    }
}
