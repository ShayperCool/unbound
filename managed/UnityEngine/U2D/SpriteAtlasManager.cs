using System;

namespace UnityEngine.U2D
{
    /// <summary>
    /// Late binding of sprite atlases: a sprite of an atlas not included in builds asks the
    /// <see cref="atlasRequested"/> listeners for its atlas by tag (once per tag, at the next
    /// frame's SpriteAtlasManagerUpdate); the atlas a listener hands back registers, and the
    /// sprites waiting for it bind to its pages (see SpriteAtlasPacking).
    /// </summary>
    public partial class SpriteAtlasManager
    {
        public static event Action<string, Action<SpriteAtlas>> atlasRequested;

        public static event Action<SpriteAtlas> atlasRegistered;

        /// <summary>Asks the listeners for an atlas; false when nobody listens.</summary>
        internal static bool RequestAtlas(string tag)
        {
            var listeners = atlasRequested;
            if (listeners == null) return false;
            listeners(tag, Register);
            return true;
        }

        internal static void Register(SpriteAtlas spriteAtlas)
        {
            SpriteAtlasPacking.Register(spriteAtlas);
            atlasRegistered?.Invoke(spriteAtlas);
        }
    }
}
