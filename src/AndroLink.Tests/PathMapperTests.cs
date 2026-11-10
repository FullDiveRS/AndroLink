using AndroLink.Core;

namespace AndroLink.Tests;

public class PathMapperTests
{
    private static readonly DeviceConfig Device = new() { Id = "dev-test1234", Name = "Test" };
    private const string SharedRoot = "/tmp/androlink-test"; // works on Windows too as a literal in these string tests

    [Fact]
    public void Sdcard_maps_to_device_folder()
    {
        var windows = PathMapper.ToWindowsPath(Device, SharedRoot, "/sdcard");
        Assert.NotNull(windows);
        Assert.EndsWith(Path.Combine("userdata", "dev-test1234", "sdcard"), windows!);
    }

    [Fact]
    public void Sdcard_subpath_maps_recursively()
    {
        var windows = PathMapper.ToWindowsPath(Device, SharedRoot, "/sdcard/DCIM/Camera/photo.jpg");
        Assert.NotNull(windows);
        Assert.Contains(Path.Combine("DCIM", "Camera", "photo.jpg"), windows!);
    }

    [Fact]
    public void Paths_outside_sdcard_are_not_mapped()
    {
        Assert.Null(PathMapper.ToWindowsPath(Device, SharedRoot, "/data/app/foo"));
        Assert.Null(PathMapper.ToWindowsPath(Device, SharedRoot, "/system/build.prop"));
    }

    [Fact]
    public void Traversal_is_rejected()
    {
        Assert.Null(PathMapper.ToWindowsPath(Device, SharedRoot, "/sdcard/../../etc/passwd"));
        Assert.False(PathMapper.IsSafeRelative("../evil"));
    }

    [Fact]
    public void Windows_path_round_trips_to_android()
    {
        var windows = PathMapper.ToWindowsPath(Device, SharedRoot, "/sdcard/Download/readme.txt")!;
        var android = PathMapper.ToAndroidPath(Device, SharedRoot, windows);
        Assert.Equal("/sdcard/Download/readme.txt", android);
    }

    [Fact]
    public void Windows_path_outside_sdcard_returns_null()
    {
        Assert.Null(PathMapper.ToAndroidPath(Device, SharedRoot, "/tmp/other/place.txt"));
    }

    [Fact]
    public void RelativeUnderSdcard_handles_edge_cases()
    {
        Assert.Equal("", PathMapper.RelativeUnderSdcard("/sdcard"));
        Assert.Equal("DCIM", PathMapper.RelativeUnderSdcard("/sdcard/DCIM/"));
        Assert.Null(PathMapper.RelativeUnderSdcard("/sdcardx"));
    }
}
