using Engine.Core;
using Engine.Platform.Base;

namespace Engine.Platform.Web;

/// <summary>
/// Texture manager backed by JavaScript OffscreenCanvas objects.
/// Texture handles are integer IDs cast to <see cref="nint"/>.
/// </summary>
public class WebTextureManager : BaseTextureManager
{
    public override nint CreateTextureFromPixels(byte[] pixels, int width, int height,
        TextureScaleMode scaleMode = TextureScaleMode.Linear)
    {
        int mode = scaleMode == TextureScaleMode.Nearest ? 0 : 1;
        int id = JsTexture.Create(pixels, width, height, mode);
        return (nint)id;
    }

    public override void DestroyTexture(nint texture)
    {
        if (texture != nint.Zero)
            JsTexture.Destroy((int)texture);
    }
}
