namespace Engine.Platform.Null;

/// <summary>
/// No-op texture manager for headless/server use. Returns dummy handles.
/// </summary>
public sealed class NullTextureManager : ITextureManager
{
    private nint _nextHandle = 1;

    public nint CreateTextureFromPixels(byte[] pixels, int width, int height,
        TextureScaleMode scaleMode = TextureScaleMode.Linear)
        => _nextHandle++;

    public void DestroyTexture(nint texture) { }
}
