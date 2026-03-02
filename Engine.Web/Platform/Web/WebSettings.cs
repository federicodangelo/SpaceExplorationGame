namespace Engine.Platform.Web;

/// <summary>
/// Settings persistence using browser localStorage via JavaScript interop.
/// </summary>
public sealed class WebSettings : ISettings
{
    public void Save(string key, string value)
    {
        try { JsSettings.Save(key, value); } catch { }
    }

    public string? Load(string key)
    {
        try { return JsSettings.Load(key); } catch { return null; }
    }
}
