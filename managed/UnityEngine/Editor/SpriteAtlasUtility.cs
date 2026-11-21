using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

namespace UnityEditor.U2D
{
    /// <summary>
    /// Editor utility for packing V1 <see cref="SpriteAtlas"/> assets and managing sprite atlas
    /// packing state (matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaspack).
    /// </summary>
    public partial class SpriteAtlasUtility
    {
        static bool s_V2ImportEnabled;

        public SpriteAtlasUtility() { }

        internal static bool V2ImportEnabled => s_V2ImportEnabled;

        internal static void EnableV2Import(bool onOff)
        {
            s_V2ImportEnabled = onOff;
        }

        public static void CleanupAtlasPacking()
        {
            s_V2ImportEnabled = false;
        }

        public static void PackAtlases(SpriteAtlas[] atlases, BuildTarget target, bool canCancel = true)
        {
            if (atlases == null)
                throw new ArgumentNullException("atlases", "Value for parameter atlases is null");
            for (int i = 0; i < atlases.Length; i++)
            {
                if (atlases[i] == null)
                    throw new ArgumentNullException("atlases", "One of the elements in atlases is null. Please check your Inputs.");
            }
            PackAtlasesInternal(atlases, target, canCancel, false, false);
        }

        public static void PackAllAtlases(BuildTarget target, bool canCancel = true)
        {
            int mode = EditorSettings.SpritePackerModeValue;
            if (mode != EditorSettings.SpritePackerModeBuildTimeOnlyAtlas
                && mode != EditorSettings.SpritePackerModeAlwaysOnAtlas
                && mode != EditorSettings.SpritePackerModeSpriteAtlasV2
                && mode != EditorSettings.SpritePackerModeSpriteAtlasV2Build)
                return;

            var guids = AssetDatabase.FindAssets("t:SpriteAtlas");
            if (guids == null || guids.Length == 0) return;

            var paths = new List<string>(guids.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path) && seen.Add(path))
                    paths.Add(path);
            }
            paths.Sort(StringComparer.Ordinal);

            var v1 = new List<SpriteAtlas>(paths.Count);
            for (int i = 0; i < paths.Count; i++)
            {
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(paths[i]);
                if (atlas != null && atlas.IsPersistentAsset && !atlas.IsFromImporter)
                    v1.Add(atlas);
            }
            if (v1.Count > 0)
                PackAtlasesInternal(v1.ToArray(), target, canCancel, false, false);
        }

        internal static void PackAtlasesInternal(SpriteAtlas[] atlases, BuildTarget target, bool canCancel = true, bool invokedFromImporter = false, bool unloadSprites = false)
        {
            _ = canCancel;
            _ = invokedFromImporter;
            _ = unloadSprites;
            if (atlases == null || atlases.Length == 0) return;

            var v1 = new List<SpriteAtlas>(atlases.Length);
            for (int i = 0; i < atlases.Length; i++)
            {
                var atlas = atlases[i];
                if (atlas == null) continue;
                if (atlas.IsFromImporter) continue;
                if (!atlas.IsPersistentAsset)
                {
                    Debug.LogWarning("Packing non-persistent SpriteAtlases are not supported and were skipped.");
                    continue;
                }
                v1.Add(atlas);
            }
            if (v1.Count == 0) return;

            SpriteAtlasPacking.PackV1(v1, target);
        }
    }
}
