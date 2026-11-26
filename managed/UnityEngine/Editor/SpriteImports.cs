using System;
using System.Collections.Generic;
using System.Globalization;
using Unbound.Runtime;
using UnityEditor;

namespace UnityEditor
{
    /// <summary>
    /// What a texture's import gives its sprites as the Editor imports them (matches the Unity
    /// 6000.0.71f1 Editor, tests/projects/spritemeta): a Single sprite without a sprite ID gets the
    /// Editor's (<see cref="SingleSpriteId"/>), and its .meta is then written with it, without NPOT
    /// scaling (sprites are never scaled), in the release's layout; a sprite whose .meta has an ID
    /// is left as it is, NPOT scaling read as none. Measured at a project's open, for the textures
    /// its import found new or changed; an import later in the session (ImportAsset,
    /// SaveAndReimport) and Multiple sprites without IDs are not measured and keep their .meta.
    /// Other releases keep the previous model (<see cref="Compat.SpriteFirstImportMeasured"/>).
    /// </summary>
    internal static class SpriteImports
    {
        /// <summary>Unity's class name of a sprite, whose file id hash names its ID.</summary>
        const string SpriteClass = "Sprite";

        /// <summary>The third word of an Editor-made sprite ID (crates/unbound-assets meta::sprite_id).</summary>
        const uint SpriteIdWord = 0x80;

        /// <summary>The sprite ID the Editor gives a sprite whose file id is <paramref name="fileId"/>:
        /// a GUID of the id's low and high words and 0x80, in Unity's GUID text.</summary>
        internal static string SpriteId(long fileId)
        {
            ulong id = (ulong)fileId;
            return Word((uint)id) + Word((uint)(id >> 32)) + Word(SpriteIdWord) + Word(0);
        }

        static string Word(uint word)
        {
            var hex = word.ToString("x8", CultureInfo.InvariantCulture).ToCharArray();
            Array.Reverse(hex);
            return new string(hex);
        }

        /// <summary>The ID of a texture's single sprite: that of a sprite of no name, whatever the
        /// texture (tests/projects/spritemeta sprites.EmptyId, sprites.Bare).</summary>
        internal static string SingleSpriteId =>
            SpriteId((long)ScriptedImports.XxHash64(System.Text.Encoding.UTF8.GetBytes("Type:" + SpriteClass + "->0")));

        /// <summary>The project's import at the session's start found <paramref name="imported"/>:
        /// each Single sprite among them without a sprite ID gets it, and its .meta is written.</summary>
        internal static void Imported(IEnumerable<string> imported)
        {
            if (!Compat.SpriteFirstImportMeasured) return;
            foreach (var path in imported)
            {
                // A texture's main asset is its Texture2D (asked without loading it).
                if (AssetDatabase.IsImmutablePackagePath(path) || AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(UnityEngine.Texture2D)) continue;
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer || importer.m_Settings == null) continue;
                if (MetaSettings.Int(importer.m_Settings, "textureType") != (int)TextureImporterType.Sprite
                    || MetaSettings.Int(importer.m_Settings, "spriteMode") != (int)SpriteImportMode.Single) continue;
                if (!string.IsNullOrEmpty(MetaSettings.Find(importer.m_Settings, "spriteSheet.spriteID")?.AsString())) continue;
                MetaSettings.Set(importer.m_Settings, "spriteSheet.spriteID", SingleSpriteId);
                MetaSettings.SetInt(importer.m_Settings, "nPOTScale", (int)TextureImporterNPOTScale.None);
                importer.Changed();
                ImporterMeta.Write(importer);
            }
        }

        /// <summary>The NPOT scaling a texture's settings read as: none for a sprite, whatever its
        /// .meta holds (tests/projects/spritemeta importer.IdNpot).</summary>
        internal static TextureImporterNPOTScale NpotScaleOf(TextureImporterType type, TextureImporterNPOTScale saved) =>
            type == TextureImporterType.Sprite && Compat.SpriteFirstImportMeasured ? TextureImporterNPOTScale.None : saved;
    }
}
