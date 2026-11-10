using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace AndroLink.Core.SystemImages;

/// <summary>Progress payload for system image downloads.</summary>
public sealed class SystemImageProgress
{
    public string PackagePath { get; init; } = "";
    /// <summary>0-100 during download; extraction reports 100*entryCount/total.</summary>
    public int Percent { get; init; }
    public long ReceivedBytes { get; init; }
    public long TotalBytes { get; init; }
    /// <summary>"Downloading", "Verifying", "Extracting", "Done".</summary>
    public string Stage { get; init; } = "";
}

/// <summary>
/// Installs Android system images into %LOCALAPPDATA%\AndroLink\system-images
/// (the AndroLink equivalent of the SDK folder), the same way Android
/// Studio's Device Manager downloads and unpacks them:
/// download -> SHA-1 verify -> extract -> marker json.
///
/// Layout: system-images/android-&lt;api&gt;/&lt;tag&gt;/&lt;abi&gt;/... which
/// intentionally mirrors an Android SDK folder, so the Emulator backend can
/// point ANDROID_SDK_ROOT at the root and use image.sysdir.1 directly.
/// </summary>
public sealed class SystemImageStore
{
    private readonly string _root;
    private readonly object _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _downloads = new(StringComparer.OrdinalIgnoreCase);

    public SystemImageStore(string? root = null)
        => _root = root ?? AndroLinkPaths.SystemImagesRoot;

    /// <summary>Root folder that mirrors an Android SDK layout.</summary>
    public string Root => _root;

    // ---------------------------------------------------------------- events

    /// <summary>Raised on background threads for every progress tick.</summary>
    public event Action<SystemImageProgress>? Progress;

    /// <summary>Raised when a download finishes (or fails; check Error).</summary>
    public event Action<SystemImageInfo, string?>? DownloadFinished;

    // --------------------------------------------------------------- queries

    public string InstallDirFor(SystemImageInfo image)
        => Path.Combine(_root, "system-images", $"android-{image.ApiLevel}", image.TagId, image.Abi);

    public string MarkerFileFor(SystemImageInfo image)
        => Path.Combine(InstallDirFor(image), "androlink-image.json");

    public bool IsInstalled(SystemImageInfo image) => File.Exists(MarkerFileFor(image));

    public bool IsDownloading(SystemImageInfo image)
    {
        lock (_gate) return _downloads.ContainsKey(image.PackagePath);
    }

    /// <summary>Installed images, discovered from the on-disk markers.</summary>
    public IReadOnlyList<SystemImageInfo> GetInstalled()
    {
        var result = new List<SystemImageInfo>();
        var imagesRoot = Path.Combine(_root, "system-images");
        if (!Directory.Exists(imagesRoot)) return result;

        foreach (var marker in Directory.EnumerateFiles(imagesRoot, "androlink-image.json", SearchOption.AllDirectories))
        {
            try
            {
                var stored = Json.Load<StoredImage>(marker);
                if (stored?.ToInfo() is { } info)
                {
                    result.Add(info);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("images", $"unreadable marker {marker}: {ex.Message}");
            }
        }

        return result.OrderByDescending(i => i.ApiLevel).ThenBy(i => i.TagId).ThenBy(i => i.Abi).ToList();
    }

    public SystemImageInfo? FindInstalled(string packagePath)
        => GetInstalled().FirstOrDefault(i => i.PackagePath.Equals(packagePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Windows folder that backs a device /sdcard-style image path, for "Reveal in Explorer".</summary>
    public string? TryGetInstallDir(string packagePath)
    {
        var info = FindInstalled(packagePath);
        var dir = info is null ? null : InstallDirFor(info);
        return dir is not null && Directory.Exists(dir) ? dir : null;
    }

    // -------------------------------------------------------------- download

    /// <summary>
    /// Downloads, verifies and installs one system image. Progress events are
    /// raised on background threads; returns the stored info or throws.
    /// </summary>
    public async Task<SystemImageInfo> DownloadAsync(
        SystemImageInfo image,
        CancellationToken ct = default,
        IProgress<SystemImageProgress>? progress = null)
    {
        lock (_gate)
        {
            if (_downloads.ContainsKey(image.PackagePath))
            {
                throw new InvalidOperationException($"'{image.PackagePath}' is already downloading.");
            }
            _downloads[image.PackagePath] = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }

        try
        {
            var cts = GetCts(image.PackagePath)!;
            var token = cts.Token;

            Directory.CreateDirectory(_root);
            var staging = Path.Combine(_root, ".staging");
            Directory.CreateDirectory(staging);
            var partFile = Path.Combine(staging, ToFileName(image.PackagePath) + ".zip.part");

            Report(progress, image, 0, 0, image.SizeBytes, "Downloading");
            Log.Info("images", $"downloading {image.PackagePath} ({FormatBytes(image.SizeBytes)}) from {image.Url}");

            // 1) Download with progress + sha1 while streaming.
            long received = 0;
            string? sha1 = null;
            using (var response = await SystemImageCatalog.SharedClient.GetAsync(image.Url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? image.SizeBytes;

                await using var http = await response.Content.ReadAsStreamAsync(token);
                await using var output = File.Create(partFile);
                using var hasher = SHA1.Create();

                var buffer = new byte[1024 * 512];
                int read;
                var lastReport = DateTime.MinValue;
                while ((read = await http.ReadAsync(buffer, token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    hasher.TransformBlock(buffer, 0, read, null, 0);
                    received += read;

                    if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 120)
                    {
                        lastReport = DateTime.UtcNow;
                        Report(progress, image, Percent(received, total), received, total, "Downloading");
                    }
                }
                hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                sha1 = Convert.ToHexString(hasher.Hash ?? Array.Empty<byte>()).ToLowerInvariant();
            }

            Report(progress, image, 100, received, received, "Verifying");

            // 2) Verify checksum.
            if (!string.IsNullOrEmpty(image.Sha1)
                && !sha1.Equals(image.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Checksum mismatch for {image.PackagePath}: expected {image.Sha1}, got {sha1}.");
            }

            // 3) Extract into the SDK-style layout.
            var target = InstallDirFor(image);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
            Directory.CreateDirectory(target);

            Report(progress, image, 100, received, received, "Extracting");
            var entries = ExtractZip(partFile, target, $"system-images/android-{image.ApiLevel}/{image.TagId}/{image.Abi}",
                p => Report(progress, image, p, received, received, "Extracting"));

            // 4) Marker + cleanup.
            var stored = StoredImage.From(image, received, sha1, DateTime.UtcNow);
            Json.Save(MarkerFileFor(image), stored);
            TryDelete(partFile);

            Report(progress, image, 100, received, received, "Done");
            DownloadFinished?.Invoke(image, null);
            Log.Info("images", $"installed {image.PackagePath} -> {target} ({entries} files)");
            return stored.ToInfo() ?? image;
        }
        catch (OperationCanceledException)
        {
            DownloadFinished?.Invoke(image, "cancelled");
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("images", $"download failed for {image.PackagePath}: {ex.Message}");
            DownloadFinished?.Invoke(image, ex.Message);
            throw;
        }
        finally
        {
            CancellationTokenSource? done;
            lock (_gate)
            {
                _downloads.TryGetValue(image.PackagePath, out done);
                _downloads.Remove(image.PackagePath);
            }
            done?.Dispose();
        }
    }

    public void CancelDownload(string packagePath)
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (!_downloads.TryGetValue(packagePath, out cts)) return;
        }
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { }
        Log.Info("images", $"download cancelled: {packagePath}");
    }

    // --------------------------------------------------------------- removal

    public void Delete(SystemImageInfo image)
    {
        var dir = InstallDirFor(image);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
            Log.Info("images", $"deleted {image.PackagePath} ({dir})");
        }
        // Prune now-empty api/tag folders so the store stays tidy.
        PruneEmpty(Path.Combine(_root, "system-images"));
    }

    // ------------------------------------------------------------- internals

    /// <summary>
    /// Extracts a system image zip. Google's zips root everything under
    /// system-images/android-&lt;api&gt;/&lt;tag&gt;/&lt;abi&gt;/; the prefix is
    /// stripped into targetDir. Zips without that prefix are extracted as-is.
    /// Returns the number of files written.
    /// </summary>
    public static int ExtractZip(string zipPath, string targetDir, string stripPrefix, Action<int>? percent)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var fileEntries = archive.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .ToList();
        if (fileEntries.Count == 0) return 0;

        var prefix = stripPrefix.Replace('\\', '/').TrimEnd('/');
        var hasPrefix = fileEntries.Any(e => e.FullName.Replace('\\', '/').StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));

        var done = 0;
        foreach (var entry in fileEntries)
        {
            var rel = entry.FullName.Replace('\\', '/');
            if (hasPrefix)
            {
                var idx = rel.IndexOf(prefix + "/", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                rel = rel[(idx + prefix.Length + 1)..];
            }
            if (rel.Length == 0) continue;

            rel = rel.Replace('/', Path.DirectorySeparatorChar);
            if (rel.Contains("..") || Path.IsPathRooted(rel))
            {
                Log.Warn("images", $"skipping suspicious zip entry: {entry.FullName}");
                continue;
            }

            var outPath = Path.Combine(targetDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            using var source = entry.Open();
            using var dest = File.Create(outPath);
            source.CopyTo(dest);
            done++;

            if (percent is not null && done % 16 == 0)
            {
                percent(Math.Min(99, 100 * done / fileEntries.Count));
            }
        }
        percent?.Invoke(100);
        return done;
    }

    private static void PruneEmpty(string? dir)
    {
        if (dir is null || !Directory.Exists(dir)) return;
        foreach (var sub in Directory.GetDirectories(dir))
        {
            PruneEmpty(sub);
            if (Directory.GetFiles(sub).Length == 0 && Directory.GetDirectories(sub).Length == 0)
            {
                try { Directory.Delete(sub); }
                catch (IOException) { }
            }
        }
    }

    private CancellationTokenSource? GetCts(string packagePath)
    {
        lock (_gate) return _downloads.GetValueOrDefault(packagePath);
    }

    private void Report(IProgress<SystemImageProgress>? progress, SystemImageInfo image,
        int percent, long received, long total, string stage)
    {
        var report = new SystemImageProgress
        {
            PackagePath = image.PackagePath,
            Percent = Math.Clamp(percent, 0, 100),
            ReceivedBytes = received,
            TotalBytes = total,
            Stage = stage,
        };
        progress?.Report(report);
        Progress?.Invoke(report);
    }

    internal static string ToFileName(string packagePath)
        => packagePath.Replace(';', '.').Replace('/', '.');

    internal static int Percent(long received, long total)
        => total <= 0 ? 0 : Math.Clamp((int)(100 * received / total), 0, 100);

    public static string FormatBytes(long bytes)
        => bytes switch
        {
            >= 1_000_000_000 => $"{bytes / 1_000_000_000.0:0.##} GB",
            >= 1_000_000 => $"{bytes / 1_000_000.0:0.#} MB",
            >= 1_000 => $"{bytes / 1_000.0:0.#} KB",
            _ => $"{bytes} B",
        };

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
    }
}

/// <summary>Marker json written next to every installed image.</summary>
public sealed class StoredImage
{
    public string PackagePath { get; set; } = "";
    public int ApiLevel { get; set; }
    public string TagId { get; set; } = "";
    public string TagDisplay { get; set; } = "";
    public string Abi { get; set; } = "";
    public string Url { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha1 { get; set; } = "";
    public int Revision { get; set; }
    public string Display { get; set; } = "";
    public DateTime InstalledAt { get; set; }

    public static StoredImage From(SystemImageInfo info, long downloadedBytes, string sha1, DateTime at)
        => new()
        {
            PackagePath = info.PackagePath,
            ApiLevel = info.ApiLevel,
            TagId = info.TagId,
            TagDisplay = string.IsNullOrWhiteSpace(info.TagDisplay) ? SystemImageCatalog.TagDisplay(info.TagId) : info.TagDisplay,
            Abi = info.Abi,
            Url = info.Url,
            SizeBytes = downloadedBytes > 0 ? downloadedBytes : info.SizeBytes,
            Sha1 = string.IsNullOrEmpty(sha1) ? info.Sha1 : sha1,
            Revision = info.Revision,
            Display = info.Display,
            InstalledAt = at,
        };

    public SystemImageInfo? ToInfo()
        => string.IsNullOrEmpty(PackagePath) ? null : new SystemImageInfo
        {
            PackagePath = PackagePath,
            ApiLevel = ApiLevel,
            TagId = TagId,
            TagDisplay = string.IsNullOrWhiteSpace(TagDisplay) ? SystemImageCatalog.TagDisplay(TagId) : TagDisplay,
            Abi = Abi,
            Url = Url,
            SizeBytes = SizeBytes,
            Sha1 = Sha1,
            Revision = Revision,
            Display = Display,
        };
}
