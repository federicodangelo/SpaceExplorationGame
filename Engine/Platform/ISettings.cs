namespace Engine.Platform;

/// <summary>
/// Platform-provided key/value settings persistence.
/// </summary>
public interface ISettings
{
    /// <summary>Persist a value under the given key.</summary>
    void Save(string key, string value);

    /// <summary>Load a previously saved value, or null if not found.</summary>
    string? Load(string key);
}
