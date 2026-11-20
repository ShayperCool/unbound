using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D
{
    /// <summary>
    /// The Editor's view of a sprite atlas (V1 `.spriteatlas` and V2 `.spriteatlasv2` alike), as the
    /// Unity 6000.0.71f1 Editor answers it (tests/projects/spriteatlaseditor): its packables in their
    /// stored order (a folder as its folder asset, a texture, a sprite; a packable whose asset is gone
    /// is left out), its master atlas (a variant's; null otherwise) and whether builds include it.
    /// A null atlas throws ArgumentNullException.
    /// </summary>
    public static partial class SpriteAtlasExtensions
    {
        public static Object[] GetPackables(this SpriteAtlas spriteAtlas)
        {
            if (spriteAtlas == null) throw new ArgumentNullException(nameof(spriteAtlas));
            var packables = new List<Object>();
            foreach (var packable in spriteAtlas.Packables)
            {
                if (string.IsNullOrEmpty(packable.Guid)) continue;
                var path = AssetDatabase.GUIDToAssetPath(packable.Guid);
                if (string.IsNullOrEmpty(path)) continue;
                var asset = AssetDatabase.IsValidFolder(path)
                    ? AssetDatabase.LoadAssetAtPath(path, typeof(Object))
                    : AssetDatabaseRuntime.Load(packable.Guid, packable.FileId, null);
                if (asset != null) packables.Add(asset);
            }
            return packables.ToArray();
        }

        public static SpriteAtlas GetMasterAtlas(this SpriteAtlas spriteAtlas)
        {
            if (spriteAtlas == null) throw new ArgumentNullException(nameof(spriteAtlas));
            return spriteAtlas.MasterAtlas;
        }

        public static bool IsIncludeInBuild(this SpriteAtlas spriteAtlas)
        {
            if (spriteAtlas == null) throw new ArgumentNullException(nameof(spriteAtlas));
            return spriteAtlas.IncludeInBuild;
        }
    }
}
