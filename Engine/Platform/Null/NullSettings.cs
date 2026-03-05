namespace Engine.Platform.Null;

/// <summary>
/// No-op settings for headless/server use. Values are stored in-memory only.
/// </summary>
public sealed class NullSettings : ISettings
{
    private readonly Dictionary<string, string> _store = new();

    public void Save(string key, string value) => _store[key] = value;
    public string? Load(string key) => _store.TryGetValue(key, out var v) ? v : null;
}
