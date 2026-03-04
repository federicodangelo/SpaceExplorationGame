
namespace Engine.Platform.Base;

/// <summary>
/// Abstract base for SDL and Web texture managers.
/// Eliminates the duplicate <c>SetPixelBlock</c> static that each implementation
/// previously declared alongside the identical version on <see cref="ITextureManager"/>.
/// </summary>
public abstract class BaseTextureManager : ITextureManager
{
    public abstract nint CreateTextureFromPixels(byte[] pixels, int width, int height,
        TextureScaleMode scaleMode = TextureScaleMode.Linear);

    public abstract void DestroyTexture(nint texture);
}
