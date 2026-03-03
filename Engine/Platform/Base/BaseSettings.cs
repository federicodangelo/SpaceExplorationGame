namespace Engine.Platform.Base;

/// <summary>
/// Abstract base for SDL and Web settings implementations.
/// </summary>
public abstract class BaseSettings : ISettings
{
    public abstract void Save(string key, string value);
    public abstract string? Load(string key);
}
