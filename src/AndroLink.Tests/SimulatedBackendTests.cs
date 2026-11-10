using AndroLink.Backend.Simulated;
using AndroLink.Core;

namespace AndroLink.Tests;

/// <summary>
/// End-to-end backend lifecycle against a temp shared root: boot, sample
/// files, APK install into the live shared folder, catalog updates.
/// </summary>
public class SimulatedBackendTests
{
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), $"al-root-{Guid.NewGuid():N}");

    [Fact]
    public async Task Boot_creates_shared_folders_and_sample_files()
    {
        var root = NewRoot();
        try
        {
            var backend = new SimulatedBackend();
            backend.Initialize(new BackendContext { SharedRoot = root });

            var device = new DeviceConfig { Name = "Demo", BackendId = "simulated" };
            await backend.StartAsync(device);

            var status = await backend.GetStatusAsync(device);
            Assert.Equal(DeviceRunState.Running, status.State);

            var sdcard = AndroLinkPaths.DeviceSdcardDir(root, device.Id);
            Assert.True(Directory.Exists(Path.Combine(sdcard, "Download")));
            Assert.True(Directory.Exists(Path.Combine(sdcard, "DCIM")));
            Assert.True(File.Exists(Path.Combine(sdcard, "Download", "welcome.txt")));

            await backend.StopAsync(device);
            var stopped = await backend.GetStatusAsync(device);
            Assert.Equal(DeviceRunState.Stopped, stopped.State);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallAppAsync_copies_apk_into_shared_folder_and_updates_catalog()
    {
        var root = NewRoot();
        try
        {
            var backend = new SimulatedBackend();
            backend.Initialize(new BackendContext { SharedRoot = root });

            var device = new DeviceConfig { Name = "Demo", BackendId = "simulated" };
            await backend.StartAsync(device);

            var apkPath = TestApkBuilder.WriteTestApk();
            var progressReports = new List<InstallProgress>();
            var progress = new Progress<InstallProgress>(p => progressReports.Add(p));

            await backend.InstallAppAsync(device, apkPath, progress);

            var appsDir = Path.Combine(
                AndroLinkPaths.DeviceSdcardDir(root, device.Id), "Android", "AndroLink", "apps");
            var copied = Path.Combine(appsDir, TestApkBuilder.Package + ".apk");
            Assert.True(File.Exists(copied));

            var catalog = InstalledAppCatalog.LoadFor(device, root);
            var app = Assert.Single(catalog.Apps);
            Assert.Equal(TestApkBuilder.Package, app.Package);
            Assert.Equal(TestApkBuilder.Label, app.Label);

            var listed = await backend.ListInstalledAppsAsync(device);
            Assert.Single(listed);

            await backend.StopAsync(device);
            File.Delete(apkPath);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UninstallAppAsync_removes_catalog_entry_and_apk_copy()
    {
        var root = NewRoot();
        try
        {
            var backend = new SimulatedBackend();
            backend.Initialize(new BackendContext { SharedRoot = root });

            var device = new DeviceConfig { Name = "Demo", BackendId = "simulated" };
            await backend.StartAsync(device);

            var apkPath = TestApkBuilder.WriteTestApk();
            await backend.InstallAppAsync(device, apkPath);
            File.Delete(apkPath);

            await backend.UninstallAppAsync(device, TestApkBuilder.Package);

            var catalog = InstalledAppCatalog.LoadFor(device, root);
            Assert.Empty(catalog.Apps);

            var copied = Path.Combine(
                AndroLinkPaths.DeviceSdcardDir(root, device.Id),
                "Android", "AndroLink", "apps", TestApkBuilder.Package + ".apk");
            Assert.False(File.Exists(copied));

            await backend.StopAsync(device);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GAppsPlan_validates_zip_input()
    {
        var missing = new GAppsPlan { ZipPath = Path.Combine(Path.GetTempPath(), "does-not-exist-xyz.zip") };
        Assert.NotNull(missing.Validate());

        Assert.Null(new GAppsPlan { ZipPath = null }.Validate());
    }

    [Fact]
    public async Task StateChanged_events_fire_during_boot()
    {
        var root = NewRoot();
        try
        {
            var backend = new SimulatedBackend();
            backend.Initialize(new BackendContext { SharedRoot = root });

            var events = new List<DeviceRunState>();
            backend.StateChanged += (_, e) => events.Add(e.Status.State);

            var device = new DeviceConfig { Name = "Demo", BackendId = "simulated" };
            await backend.StartAsync(device);

            Assert.Contains(DeviceRunState.Starting, events);
            Assert.Equal(DeviceRunState.Running, events[^1]);

            await backend.StopAsync(device);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
