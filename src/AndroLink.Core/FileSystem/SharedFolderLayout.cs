using System.Text;

namespace AndroLink.Core;

/// <summary>
/// Creates and documents the on-disk shared folder layout for a device:
///
///   &lt;SharedRoot&gt;\userdata\&lt;deviceId&gt;\
///     ├─ sdcard\                     (the device's /sdcard - user-editable)
///     │   ├─ DCIM\  Download\  Documents\  Music\  Movies\  Pictures\
///     │   └─ Android\data\  Android\obb\  Android\AndroLink\apps\
///     ├─ logs\
///     └─ appcatalog.json             (installed apps index)
/// </summary>
public static class SharedFolderLayout
{
    public static readonly string[] StandardSdcardDirs =
    {
        "DCIM",
        "Download",
        "Documents",
        "Music",
        "Movies",
        "Pictures",
        "Android/data",
        "Android/obb",
        "Android/AndroLink/apps",
    };

    /// <summary>Creates the folder tree if missing and returns the sdcard directory.</summary>
    public static string EnsureDeviceFolders(DeviceConfig device, string sharedRoot)
    {
        var dataDir = AndroLinkPaths.DeviceDataDir(sharedRoot, device.Id);
        var sdcard = Path.Combine(dataDir, "sdcard");

        Directory.CreateDirectory(sdcard);
        foreach (var dir in StandardSdcardDirs)
        {
            Directory.CreateDirectory(Path.Combine(sdcard, dir.Replace('/', Path.DirectorySeparatorChar)));
        }

        Directory.CreateDirectory(AndroLinkPaths.DeviceLogDir(sharedRoot, device.Id));

        WriteMarker(dataDir, device);
        WriteReadme(sdcard, device);
        return sdcard;
    }

    private static void WriteMarker(string dataDir, DeviceConfig device)
    {
        Json.Save(Path.Combine(dataDir, ".androlink-device.json"), new DeviceMarker
        {
            DeviceId = device.Id,
            Name = device.Name,
            Backend = device.BackendId,
            CreatedAt = device.CreatedAt,
            AndroidPath = PathMapper.AndroidSdcardRoot,
            Warning = "Folder names inside 'sdcard' are case-sensitive to Android; keep them as-is when possible.",
        });
    }

    private static void WriteReadme(string sdcard, DeviceConfig device)
    {
        var readme = Path.Combine(sdcard, "README.txt");
        if (File.Exists(readme)) return;

        var sb = new StringBuilder();
        sb.AppendLine($"AndroLink shared storage for device '{device.Name}' ({device.Id})");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();
        sb.AppendLine("This folder IS the /sdcard storage of your AndroLink Android device.");
        sb.AppendLine("Everything here is a plain Windows file - you can read, edit, copy,");
        sb.AppendLine("rename or delete it with Explorer, editors, backup tools, anything,");
        sb.AppendLine("even while the device is running in AndroLink.");
        sb.AppendLine();
        sb.AppendLine("Map (Android path -> this disk):");
        sb.AppendLine($"  /sdcard            -> {sdcard}");
        sb.AppendLine($"  /sdcard/Download   -> {Path.Combine(sdcard, "Download")}");
        sb.AppendLine($"  /sdcard/DCIM       -> {Path.Combine(sdcard, "DCIM")}");
        sb.AppendLine();
        sb.AppendLine("Tips:");
        sb.AppendLine("  * APKs you install land in Android\\AndroLink\\apps (and the device).");
        sb.AppendLine("  * Dropping files into Download makes them appear on the device.");
        sb.AppendLine("  * Avoid paths longer than 260 characters; Android apps dislike them.");
        File.WriteAllText(readme, sb.ToString(), new UTF8Encoding(false));
    }
}

public sealed class DeviceMarker
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Backend { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string AndroidPath { get; set; } = "";
    public string? Warning { get; set; }
}
