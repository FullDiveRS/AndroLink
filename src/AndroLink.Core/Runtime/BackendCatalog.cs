using System.Reflection;
using System.Runtime.Loader;

namespace AndroLink.Core;

/// <summary>
/// Discovers and owns every IRuntimeBackend: built-ins registered by the host
/// plus plugins loaded from backends/*.dll (install dir and
/// %LOCALAPPDATA%\AndroLink\backends). Plugins are plain class libraries that
/// ship one or more public classes implementing IRuntimeBackend with a
/// parameterless constructor - see docs/PLUGIN_AUTHORING.md.
/// </summary>
public sealed class BackendCatalog : IDisposable
{
    private readonly Dictionary<string, IRuntimeBackend> _backends = new(StringComparer.OrdinalIgnoreCase);
    private BackendContext? _context;
    private readonly List<string> _pluginFiles = new();

    public IReadOnlyCollection<IRuntimeBackend> All => _backends.Values;
    public IReadOnlyList<string> LoadedPluginFiles => _pluginFiles;

    public event Action<string, bool, string>? PluginLoadReported; // (file, ok, message)

    /// <summary>Raised for every backend that becomes available (built-in or plugin).</summary>
    public event Action<IRuntimeBackend>? BackendAdded;

    public IRuntimeBackend? Find(string? backendId)
        => backendId is not null && _backends.TryGetValue(backendId, out var b) ? b : null;

    /// <summary>Registers a built-in backend compiled into the host.</summary>
    public void RegisterBuiltIn(IRuntimeBackend backend)
    {
        if (_backends.ContainsKey(backend.Id))
        {
            Log.Warn("backends", $"backend id '{backend.Id}' already registered; ignoring {backend.GetType().Name}");
            return;
        }
        _backends[backend.Id] = backend;
        _context?.CallInitialize(backend);
        Log.Info("backends", $"registered backend '{backend.Id}' ({backend.DisplayName})");
        BackendAdded?.Invoke(backend);
    }

    /// <summary>
    /// Scans a directory for plugin assemblies. Every concrete IRuntimeBackend
    /// with a parameterless constructor is instantiated and initialized.
    /// </summary>
    public int LoadPluginsFromDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return 0;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.dll"))
        {
            try
            {
                var assembly = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(file), isCollectible: false)
                    .LoadFromAssemblyPath(file);
                var types = assembly.GetTypes().Where(t => t.IsRuntimeBackendPlugin()).ToList();
                foreach (var type in types)
                {
                    var backend = (IRuntimeBackend)Activator.CreateInstance(type)!;
                    RegisterBuiltIn(backend); // reuses duplicate-id protection; plugins are built-ins once loaded
                    _pluginFiles.Add(file);
                    count++;
                }
                PluginLoadReported?.Invoke(file, true, $"{types.Count} backend(s)");
            }
            catch (BadImageFormatException ex)
            {
                PluginLoadReported?.Invoke(file, false, ex.Message);
                Log.Warn("backends", $"skipped {file}: not a managed assembly for this runtime");
            }
            catch (Exception ex)
            {
                PluginLoadReported?.Invoke(file, false, ex.Message);
                Log.Error("backends", $"plugin load failed for {file}: {ex}");
            }
        }
        return count;
    }

    public void InitializeAll(BackendContext context)
    {
        _context = context;
        foreach (var backend in _backends.Values)
        {
            context.CallInitialize(backend);
        }
    }

    public void Dispose()
    {
        foreach (var backend in _backends.Values)
        {
            try { backend.Dispose(); }
            catch (Exception ex) { Log.Warn("backends", $"dispose failed for {backend.Id}: {ex.Message}"); }
        }
        _backends.Clear();
    }
}

internal static class BackendContextInitExtensions
{
    public static void CallInitialize(this BackendContext context, IRuntimeBackend backend)
    {
        try
        {
            backend.Initialize(context);
        }
        catch (Exception ex)
        {
            Log.Error("backends", $"Initialize failed for backend '{backend.Id}': {ex}");
        }
    }
}
