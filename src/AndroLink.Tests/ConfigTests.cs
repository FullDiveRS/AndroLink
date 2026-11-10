using AndroLink.Core;

namespace AndroLink.Tests;

public class ConfigTests
{
    [Fact]
    public void DeviceConfig_round_trips_through_json()
    {
        var device = new DeviceConfig
        {
            Name = "Pixel Demo",
            Width = 1080,
            Height = 2340,
            Dpi = 400,
            RamMb = 4096,
            CpuCores = 4,
            BackendId = "qemu",
            GAppsZipPath = "/tmp/gapps.zip",
            BackendOptions = { ["serial"] = "127.0.0.1:5555", ["bootTimeoutSec"] = "90" },
        };

        var path = Path.Combine(Path.GetTempPath(), $"al-cfg-{Guid.NewGuid():N}.json");
        try
        {
            Json.Save(path, device);
            var loaded = Json.Load<DeviceConfig>(path);

            Assert.Equal(device.Name, loaded.Name);
            Assert.Equal(device.Width, loaded.Width);
            Assert.Equal(device.RamMb, loaded.RamMb);
            Assert.Equal(device.BackendId, loaded.BackendId);
            Assert.Equal(device.BackendOptions["serial"], loaded.BackendOptions["serial"]);
            Assert.Equal(device.GAppsZipPath, loaded.GAppsZipPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppSettings_round_trips_the_default_image_choice()
    {
        var settings = new AppSettings
        {
            SharedRoot = @"C:\AndroLink",
            ThemeName = "light",
            DefaultImagePackage = "system-images;android-36;google_apis;x86_64",
        };

        var path = Path.Combine(Path.GetTempPath(), $"al-set-{Guid.NewGuid():N}.json");
        try
        {
            Json.Save(path, settings);
            var loaded = Json.Load<AppSettings>(path);

            Assert.Equal(settings.SharedRoot, loaded.SharedRoot);
            Assert.Equal(settings.ThemeName, loaded.ThemeName);
            Assert.Equal(settings.DefaultImagePackage, loaded.DefaultImagePackage);
            Assert.Equal(settings.MinimizeToTray, loaded.MinimizeToTray);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DeviceRegistry_creates_and_deletes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"al-reg-{Guid.NewGuid():N}.json");
        try
        {
            var registry = new DeviceRegistry(path);
            var device = registry.Create("Test device", "simulated");
            Assert.Single(registry.Devices);

            var reloaded = new DeviceRegistry(path);
            Assert.Equal(device.Id, Assert.Single(reloaded.Devices).Id);

            Assert.True(registry.Delete(device.Id));
            Assert.Empty(registry.Devices);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ApkInspector_extracts_metadata_from_a_synthetic_apk()
    {
        var apkPath = TestApkBuilder.WriteTestApk();
        try
        {
            var info = ApkInspector.Inspect(apkPath);
            Assert.NotNull(info);
            Assert.Equal("com.androlink.testapp", info!.Package);
            Assert.Equal("1.2.3", info.VersionName);
            Assert.Equal(7, info.VersionCode);
            Assert.Equal(24, info.MinSdk);
            Assert.Equal("AndroLink Test App", info.Label); // resolved via resources.arsc
            Assert.Contains("android.permission.INTERNET", info.Permissions);
        }
        finally
        {
            File.Delete(apkPath);
        }
    }

    [Fact]
    public void ApkInspector_returns_null_for_garbage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"al-bad-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
        try
        {
            Assert.Null(ApkInspector.Inspect(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
