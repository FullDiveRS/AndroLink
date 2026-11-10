using System.Xml.Linq;

namespace AndroLink.Core.SystemImages;

/// <summary>
/// Source of the downloadable system image list. Online mode downloads
/// Google's per-tag repositories (the same files the Android Studio SDK
/// Manager reads); offline mode falls back to the built-in snapshot so the
/// Images page always works.
/// </summary>
public static class SystemImageCatalog
{
    /// <summary>Base URL of every per-tag system image repository.</summary>
    public const string RepositoryBase = "https://dl.google.com/android/repository/sys-img/";

    /// <summary>Tags offered in the Images page, in display order.</summary>
    public static readonly string[] Tags =
    {
        "google_apis",
        "google_apis_playstore",
        "android",              // AOSP ("Default")
        "android-tv",
        "android-wear",
        "android-automotive-playstore",
    };

    private static readonly Dictionary<string, string> TagDisplayMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["google_apis"] = "Google APIs",
        ["google_apis_playstore"] = "Google Play",
        ["android"] = "AOSP (default)",
        ["default"] = "AOSP (default)",
        ["android-tv"] = "Android TV",
        ["android-wear"] = "Android Wear",
        ["android-automotive-playstore"] = "Automotive",
    };

    public static string TagDisplay(string tagId)
        => TagDisplayMap.GetValueOrDefault(tagId, tagId);

    /// <summary>Offline snapshot built into the binary (real Google data, stable channel).</summary>
    public static IReadOnlyList<SystemImageInfo> DefaultCatalog()
        => KnownImages.Snapshot;

    /// <summary>
    /// Downloads the live per-tag repositories and returns the merged list,
    /// newest revision per (api, tag, abi). Throws on network failure - call
    /// sites should catch and fall back to <see cref="DefaultCatalog"/>.
    /// </summary>
    public static async Task<IReadOnlyList<SystemImageInfo>> FetchOnlineAsync(
        IEnumerable<string>? tags = null, CancellationToken ct = default)
    {
        tags ??= Tags;
        var client = SharedClient;
        var results = new List<SystemImageInfo>();

        foreach (var tag in tags)
        {
            ct.ThrowIfCancellationRequested();
            var url = $"{RepositoryBase}{tag}/sys-img2-3.xml";
            using var stream = await client.GetStreamAsync(url, ct).ConfigureAwait(false);
            results.AddRange(ParseRepository(stream, tag));
        }

        if (results.Count == 0)
        {
            throw new InvalidOperationException("The system image repository did not contain any system images.");
        }

        return DedupeNewest(results);
    }

    /// <summary>Fetches online but falls back to the built-in snapshot on any failure.</summary>
    public static async Task<IReadOnlyList<SystemImageInfo>> FetchWithFallbackAsync(
        IEnumerable<string>? tags = null, CancellationToken ct = default)
    {
        try
        {
            var online = await FetchOnlineAsync(tags, ct).ConfigureAwait(false);
            return online;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn("images", $"online system image catalog unavailable ({ex.Message}); using built-in snapshot");
            return DefaultCatalog();
        }
    }

    internal static HttpClient SharedClient { get; } = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    /// <summary>
    /// Parses one per-tag sys-img repository XML (namespace-agnostic). Returns
    /// every system-image package found; see the schema sample in
    /// docs/SYSTEM_IMAGES.md.
    /// </summary>
    public static IReadOnlyList<SystemImageInfo> ParseRepository(Stream xml, string sourceTag)
    {
        var doc = XDocument.Load(xml, LoadOptions.None);
        var root = doc.Root ?? throw new InvalidOperationException("empty repository XML");
        var list = new List<SystemImageInfo>();

        foreach (var package in root.Elements().Where(e => e.Name.LocalName == "remotePackage"))
        {
            var path = package.Attribute("path")?.Value ?? "";
            var parts = path.Split(';');
            if (parts.Length != 4 || !parts[0].Equals("system-images", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var details = package.Elements().FirstOrDefault(e => e.Name.LocalName == "type-details");
            if (details is null) continue;

            var api = ReadInt(details, "api-level") ?? 0;
            var abi = ReadText(details, "abi") ?? "";
            var tagNode = details.Elements().FirstOrDefault(e => e.Name.LocalName == "tag");
            var tagId = tagNode?.Elements().FirstOrDefault(e => e.Name.LocalName == "id")?.Value.Trim() ?? sourceTag;
            var tagDisplay = tagNode?.Elements().FirstOrDefault(e => e.Name.LocalName == "display")?.Value.Trim();

            // Skip previews/canaries and unknown ABIs, mirroring Device Manager defaults.
            if (api <= 0 || !CpuAbi.IsKnown(abi)) continue;
            if (parts[1].Contains("canary", StringComparison.OrdinalIgnoreCase)
                || parts[1].Contains("beta", StringComparison.OrdinalIgnoreCase)) continue;

            var revision = package.Elements().FirstOrDefault(e => e.Name.LocalName == "revision");
            var revMajor = revision?.Elements().FirstOrDefault(e => e.Name.LocalName == "major")?.Value.Trim();

            var archive = package.Descendants().FirstOrDefault(e => e.Name.LocalName == "complete");
            if (archive is null) continue;
            var url = archive.Elements().FirstOrDefault(e => e.Name.LocalName == "url")?.Value.Trim() ?? "";
            var size = ReadInt(archive, "size") ?? 0;
            var checksum = archive.Elements().FirstOrDefault(e => e.Name.LocalName == "checksum");
            var sha1 = checksum is not null && (checksum.Attribute("type")?.Value ?? "").Equals("sha1", StringComparison.OrdinalIgnoreCase)
                ? checksum.Value.Trim() : "";

            if (url.Length == 0 || size <= 0) continue;

            list.Add(new SystemImageInfo
            {
                PackagePath = path,
                ApiLevel = api,
                TagId = tagId,
                TagDisplay = string.IsNullOrWhiteSpace(tagDisplay) ? TagDisplay(tagId) : tagDisplay,
                Abi = abi,
                Url = url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? url
                    : $"{RepositoryBase}{sourceTag}/{url}",
                SizeBytes = size,
                Sha1 = sha1,
                Revision = int.TryParse(revMajor, out var r) ? r : 0,
                Display = package.Elements().FirstOrDefault(e => e.Name.LocalName == "display-name")?.Value.Trim() ?? "",
            });
        }

        return list;
    }

    /// <summary>Keeps the newest revision per (api level, tag, abi).</summary>
    public static IReadOnlyList<SystemImageInfo> DedupeNewest(IEnumerable<SystemImageInfo> images)
        => images
            .GroupBy(i => $"{i.ApiLevel}|{i.TagId}|{i.Abi}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(i => i.Revision).First())
            .OrderByDescending(i => i.ApiLevel)
            .ThenBy(i => i.TagId, StringComparer.Ordinal)
            .ThenBy(i => Array.IndexOf(CpuAbi.All, i.Abi))
            .ToList();

    private static string? ReadText(XElement parent, string localName)
        => parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim();

    private static int? ReadInt(XElement parent, string localName)
    {
        var raw = ReadText(parent, localName);
        return raw is not null && int.TryParse(raw, out var v) ? v : null;
    }
}
