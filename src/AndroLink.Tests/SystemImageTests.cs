using System.IO.Compression;
using System.Text;
using AndroLink.Adb;
using AndroLink.Core;
using AndroLink.Core.SystemImages;

namespace AndroLink.Tests;

/// <summary>System image catalog parsing, dedupe, store extraction and AVD writing.</summary>
public class SystemImageTests
{
    private const string SampleRepo = """
        <?xml version="1.0" encoding="utf-8"?>
        <sdk:sdk-repository xmlns:sdk="http://schemas.android.com/sdk/android/repository/3"
                            xmlns:sys-img="http://schemas.android.com/sdk/android/addon/sys-img/3"
                            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <license id="android-sdk-license" type="text">terms</license>
          <channel id="channel-0"/>
          <remotePackage path="system-images;android-34;google_apis;x86_64">
            <type-details xsi:type="sys-img:sysImgDetailsType">
              <api-level>34</api-level>
              <tag><id>google_apis</id><display>Google APIs</display></tag>
              <vendor><id>google</id><display>Google Inc.</display></vendor>
              <abi>x86_64</abi>
            </type-details>
            <revision><major>14</major></revision>
            <display-name>Google APIs Intel x86_64 Atom System Image</display-name>
            <archives>
              <archive>
                <complete>
                  <size>1563721130</size>
                  <checksum type="sha1">e0f6c9a0691aa27bd597d0deb1bcfdc943ac8ca7</checksum>
                  <url>x86_64-34_r14.zip</url>
                </complete>
              </archive>
            </archives>
          </remotePackage>
          <remotePackage path="system-images;android-34;google_apis;arm64-v8a">
            <type-details xsi:type="sys-img:sysImgDetailsType">
              <api-level>34</api-level>
              <tag><id>google_apis</id><display>Google APIs</display></tag>
              <abi>arm64-v8a</abi>
            </type-details>
            <revision><major>12</major></revision>
            <display-name>Google APIs ARM 64 v8a System Image</display-name>
            <archives>
              <archive>
                <complete>
                  <size>1613000000</size>
                  <checksum type="sha1">aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa</checksum>
                  <url>arm64-v8a-34_r12.zip</url>
                </complete>
              </archive>
            </archives>
          </remotePackage>
          <remotePackage path="platforms;android-34">
            <type-details xsi:type="sdk:platformDetailsType">
              <api-level>34</api-level>
            </type-details>
          </remotePackage>
        </sdk:sdk-repository>
        """;

    [Fact]
    public void Repository_xml_parses_the_real_schema()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SampleRepo));
        var images = SystemImageCatalog.ParseRepository(stream, "google_apis");

        Assert.Equal(2, images.Count);

        var x64 = images.First(i => i.Abi == "x86_64");
        Assert.Equal("system-images;android-34;google_apis;x86_64", x64.PackagePath);
        Assert.Equal(34, x64.ApiLevel);
        Assert.Equal("google_apis", x64.TagId);
        Assert.Equal("Google APIs", x64.TagDisplay);
        Assert.Equal(1563721130, x64.SizeBytes);
        Assert.Equal("e0f6c9a0691aa27bd597d0deb1bcfdc943ac8ca7", x64.Sha1);
        Assert.Equal(14, x64.Revision);
        Assert.Equal("https://dl.google.com/android/repository/sys-img/google_apis/x86_64-34_r14.zip", x64.Url);

        var arm = images.First(i => i.Abi == "arm64-v8a");
        Assert.True(CpuAbi.IsArm(arm.Abi));
        Assert.Equal("Android 14 (API 34)", arm.VersionLabel);
    }

    [Fact]
    public void DedupeNewest_keeps_highest_revision_per_abi()
    {
        var images = new[]
        {
            new SystemImageInfo { PackagePath = "p1", ApiLevel = 34, TagId = "google_apis", Abi = "x86_64", Revision = 10 },
            new SystemImageInfo { PackagePath = "p2", ApiLevel = 34, TagId = "google_apis", Abi = "x86_64", Revision = 14 },
            new SystemImageInfo { PackagePath = "p3", ApiLevel = 34, TagId = "google_apis", Abi = "x86_64", Revision = 12 },
            new SystemImageInfo { PackagePath = "p4", ApiLevel = 34, TagId = "google_apis", Abi = "arm64-v8a", Revision = 3 },
        };

        var deduped = SystemImageCatalog.DedupeNewest(images);

        Assert.Equal(2, deduped.Count);
        Assert.Equal("p2", deduped.First(i => i.Abi == "x86_64").PackagePath);
        Assert.Equal("p4", deduped.First(i => i.Abi == "arm64-v8a").PackagePath);
    }

    [Fact]
    public void BuiltIn_snapshot_is_populated_with_realistic_entries()
    {
        var snapshot = SystemImageCatalog.DefaultCatalog();
        Assert.True(snapshot.Count > 50, $"snapshot only has {snapshot.Count} images");

        foreach (var image in snapshot)
        {
            Assert.StartsWith("system-images;", image.PackagePath);
            Assert.True(CpuAbi.IsKnown(image.Abi), image.Abi);
            Assert.StartsWith("https://dl.google.com/", image.Url);
            Assert.True(image.SizeBytes > 1_000_000);
            Assert.False(string.IsNullOrWhiteSpace(image.Sha1), image.PackagePath);
        }

        // x86_64 and arm64-v8a must both exist (x64 + ARM device support).
        Assert.Contains(snapshot, i => i.Abi == CpuAbi.X8664 && i.ApiLevel >= 34);
        Assert.Contains(snapshot, i => i.Abi == CpuAbi.Arm64V8a && i.ApiLevel >= 34);
    }

    [Fact]
    public void Store_extracts_sdk_style_zips_and_enumerates_installed()
    {
        var root = Path.Combine(Path.GetTempPath(), "androlink-store-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            // Build a fake Google-style image zip.
            var zipPath = Path.Combine(root, "fake.zip");
            Directory.CreateDirectory(root);
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                AddEntry(zip, "system-images/android-30/testtag/x86_64/system.img", "SYSTEM");
                AddEntry(zip, "system-images/android-30/testtag/x86_64/kernel-ranchu", "KERNEL");
                AddEntry(zip, "unrelated/file.txt", "nope");
            }

            var target = Path.Combine(root, "out");
            var written = SystemImageStore.ExtractZip(zipPath, target,
                "system-images/android-30/testtag/x86_64", null);

            Assert.Equal(2, written);
            Assert.Equal("SYSTEM", File.ReadAllText(Path.Combine(target, "system.img")));
            Assert.Equal("KERNEL", File.ReadAllText(Path.Combine(target, "kernel-ranchu")));
            Assert.False(File.Exists(Path.Combine(target, "unrelated")));

            // Marker round-trip: write marker, then enumerate the store.
            var store = new SystemImageStore(root);
            var info = new SystemImageInfo
            {
                PackagePath = "system-images;android-30;testtag;x86_64",
                ApiLevel = 30,
                TagId = "testtag",
                TagDisplay = "Test Tag",
                Abi = "x86_64",
                Url = "https://example.com/fake.zip",
                SizeBytes = 1234,
                Sha1 = "abc",
            };
            var marker = SystemImageStore.ExtractZip; // touch for coverage sanity
            Json.Save(store.MarkerFileFor(info), StoredImage.From(info, 1234, "abc", DateTime.UtcNow));

            Assert.True(store.IsInstalled(info));
            var installed = store.GetInstalled();
            var found = Assert.Single(installed);
            Assert.Equal(info.PackagePath, found.PackagePath);
            Assert.Equal("Test Tag", found.TagDisplay);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void AvdWriter_writes_emulator_config_pointing_at_the_image()
    {
        var avdHome = Path.Combine(Path.GetTempPath(), "androlink-avd-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var device = new DeviceConfig { Id = "dev-test1234", Width = 1080, Height = 2340, Dpi = 400, RamMb = 3072, CpuCores = 4 };
            var image = new SystemImageInfo
            {
                PackagePath = "system-images;android-34;google_apis;arm64-v8a",
                ApiLevel = 34,
                TagId = "google_apis",
                TagDisplay = "Google APIs",
                Abi = "arm64-v8a",
            };

            var name = AvdWriter.CreateAvd(avdHome, device, image, "/images-root");

            Assert.Equal("androlink_devtest1234", name);
            var config = File.ReadAllText(Path.Combine(avdHome, name + ".avd", "config.ini"));
            Assert.Contains("image.sysdir.1=system-images/android-34/google_apis/arm64-v8a/", config);
            Assert.Contains("abi.type=arm64-v8a", config);
            Assert.Contains("hw.lcd.width=1080", config);
            Assert.Contains("hw.ramSize=3072", config);

            var ini = File.ReadAllText(Path.Combine(avdHome, name + ".ini"));
            Assert.Contains($"path={Path.Combine(avdHome, name + ".avd")}", ini);
            Assert.Contains("target=android-34", ini);
        }
        finally
        {
            try { Directory.Delete(avdHome, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SdkLocator_finds_emulator_in_configured_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "androlink-sdk-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "emulator"));
            Directory.CreateDirectory(Path.Combine(root, "platform-tools"));
            var emulatorName = OperatingSystem.IsWindows() ? "emulator.exe" : "emulator";
            var adbName = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
            File.WriteAllText(Path.Combine(root, "emulator", emulatorName), "x");
            File.WriteAllText(Path.Combine(root, "platform-tools", adbName), "x");

            var sdk = AndroidSdkLocator.Locate(extraRoots: [root]);

            Assert.NotNull(sdk);
            Assert.Equal(Path.Combine(root, "emulator", emulatorName), sdk!.EmulatorExe);
            Assert.Equal(Path.Combine(root, "platform-tools", adbName), sdk.AdbExe);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CpuAbi_native_matrix_and_host_labels()
    {
        Assert.True(CpuAbi.RunsNatively("x86_64", System.Runtime.InteropServices.Architecture.X64));
        Assert.True(CpuAbi.RunsNatively("x86", System.Runtime.InteropServices.Architecture.X64));
        Assert.False(CpuAbi.RunsNatively("arm64-v8a", System.Runtime.InteropServices.Architecture.X64));
        Assert.True(CpuAbi.RunsNatively("arm64-v8a", System.Runtime.InteropServices.Architecture.Arm64));
        Assert.False(CpuAbi.RunsNatively("x86_64", System.Runtime.InteropServices.Architecture.Arm64));

        Assert.Equal(CpuAbi.X8664, HostInfo.RecommendedAbi is "x86_64" or "arm64-v8a" ? HostInfo.RecommendedAbi : "");
        Assert.False(string.IsNullOrWhiteSpace(HostInfo.ArchitectureLabel));
        Assert.False(string.IsNullOrWhiteSpace(HostInfo.Recommendation));
    }

    private static void AddEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
