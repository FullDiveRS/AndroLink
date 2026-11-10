namespace AndroLink.Core;

/// <summary>Persists the list of DeviceConfig objects to devices.json.</summary>
public sealed class DeviceRegistry
{
    private readonly List<DeviceConfig> _devices = new();
    private readonly string _filePath;

    public DeviceRegistry(string? filePath = null)
    {
        _filePath = filePath ?? AndroLinkPaths.DevicesFile;
        _devices.AddRange(Json.TryLoad<List<DeviceConfig>>(_filePath) ?? new List<DeviceConfig>());
    }

    public IReadOnlyList<DeviceConfig> Devices => _devices;

    public DeviceConfig? Find(string? id) => _devices.FirstOrDefault(d =>
        string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    public DeviceConfig Create(string name, string backendId)
    {
        var device = new DeviceConfig
        {
            Id = DeviceConfig.NewId(),
            Name = string.IsNullOrWhiteSpace(name) ? "New device" : name.Trim(),
            BackendId = backendId,
        };
        _devices.Add(device);
        Save();
        Log.Info("registry", $"created device {device.Name} [{device.Id}] on backend '{backendId}'");
        return device;
    }

    public void Update(DeviceConfig device)
    {
        var existing = Find(device.Id);
        if (existing is null)
        {
            _devices.Add(device);
        }
        else if (!ReferenceEquals(existing, device))
        {
            _devices[_devices.IndexOf(existing)] = device;
        }
        Save();
    }

    public bool Delete(string id)
    {
        var existing = Find(id);
        if (existing is null) return false;
        _devices.Remove(existing);
        Save();
        Log.Info("registry", $"deleted device {existing.Name} [{existing.Id}]");
        return true;
    }

    public void Save() => Json.Save(_filePath, _devices);
}
