using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace AndroLink.Core;

/// <summary>
/// Reads APK metadata (package, label, versions, permissions) straight from the
/// zip container: AndroidManifest.xml through the binary XML parser, and the
/// app label resolved against resources.arsc. Never throws - returns null when
/// the file is not a readable APK.
/// </summary>
public static class ApkInspector
{
    public static ApkInfo? Inspect(string apkPath)
    {
        try
        {
            if (!File.Exists(apkPath)) return null;

            byte[]? manifest = null;
            byte[]? arsc = null;

            using (var zip = ZipFile.OpenRead(apkPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName == "AndroidManifest.xml")
                    {
                        manifest = ReadAll(entry);
                    }
                    else if (entry.FullName == "resources.arsc")
                    {
                        arsc = ReadAll(entry);
                    }
                    if (manifest != null && arsc != null) break;
                }
            }

            if (manifest is null) return null;

            var root = BinaryXmlParser.Parse(manifest);
            if (!string.Equals(root.Name, "manifest", StringComparison.OrdinalIgnoreCase)) return null;

            var package = root.Attr("package")?.Text ?? "";
            if (string.IsNullOrEmpty(package)) return null;

            long versionCode = 0;
            var vc = root.Attr("versionCode");
            if (vc is not null) versionCode = vc.IntValue != 0 ? vc.IntValue : TryParseLooseLong(vc.Raw);

            var info = new ApkInfo
            {
                Package = package,
                VersionName = root.Attr("versionName")?.Text ?? "",
                VersionCode = versionCode,
                FileName = Path.GetFileName(apkPath),
                SizeBytes = new FileInfo(apkPath).Length,
            };

            var usesSdk = root.FirstChild("uses-sdk");
            if (usesSdk is not null)
            {
                var min = usesSdk.Attr("minSdkVersion");
                if (min is not null) info = WithMinSdk(info, min.IntValue != 0 ? min.IntValue : TryParseInt(min.Raw));
                var target = usesSdk.Attr("targetSdkVersion");
                if (target is not null) info = WithTargetSdk(info, target.IntValue != 0 ? target.IntValue : TryParseInt(target.Raw));
            }

            foreach (var perm in root.ChildrenNamed("uses-permission"))
            {
                var name = perm.Attr("name")?.Text;
                if (!string.IsNullOrEmpty(name)) info.Permissions.Add(name!);
            }

            var application = root.FirstChild("application");
            var label = application?.Attr("label");
            string? labelValue = null;
            if (label is not null)
            {
                labelValue = label.Text;
                if (labelValue is null && label.IsReference && arsc is not null)
                {
                    labelValue = ArscLabelResolver.ResolveStringResource(arsc, label.Data);
                }
            }

            info = WithLabel(info, string.IsNullOrWhiteSpace(labelValue) ? package : labelValue!);
            return info;
        }
        catch (Exception ex)
        {
            Log.Debug("apk", $"inspect failed for {apkPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Inspect with a graceful fallback: package/label default to the file name.</summary>
    public static ApkInfo InspectOrFallback(string apkPath)
    {
        var info = Inspect(apkPath);
        if (info is not null) return info;

        var name = Path.GetFileName(apkPath);
        return new ApkInfo
        {
            Package = Path.GetFileNameWithoutExtension(apkPath),
            Label = name,
            FileName = name,
            SizeBytes = File.Exists(apkPath) ? new FileInfo(apkPath).Length : 0,
        };
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static int TryParseInt(string? raw) => int.TryParse(raw, out var v) ? v : 0;

    private static long TryParseLooseLong(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return 0;
        var cleaned = raw.Trim();
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(cleaned[2..], System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
        {
            return hex;
        }
        return long.TryParse(cleaned, out var v) ? v : 0;
    }

    private static ApkInfo WithMinSdk(ApkInfo src, int value) => Clone(src, i => i.MinSdk = value);
    private static ApkInfo WithTargetSdk(ApkInfo src, int value) => Clone(src, i => i.TargetSdk = value);
    private static ApkInfo WithLabel(ApkInfo src, string label) => Clone(src, i => i.Label = label);

    private static ApkInfo Clone(ApkInfo src, Action<Mutable> mutate)
    {
        var m = new Mutable(src);
        mutate(m);
        return new ApkInfo
        {
            Package = src.Package,
            Label = m.Label,
            VersionName = src.VersionName,
            VersionCode = src.VersionCode,
            MinSdk = m.MinSdk,
            TargetSdk = m.TargetSdk,
            Permissions = src.Permissions,
            FileName = src.FileName,
            SizeBytes = src.SizeBytes,
        };
    }

    private sealed class Mutable
    {
        public string Label;
        public int? MinSdk;
        public int? TargetSdk;

        public Mutable(ApkInfo src)
        {
            Label = src.Label;
            MinSdk = src.MinSdk;
            TargetSdk = src.TargetSdk;
        }
    }
}
