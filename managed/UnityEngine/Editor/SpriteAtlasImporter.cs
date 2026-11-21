using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.U2D;

namespace UnityEditor.U2D
{
    /// <summary>
    /// The importer of a V2 sprite atlas (`.spriteatlasv2`), reading and writing its per-platform
    /// <see cref="TextureImporterPlatformSettings"/>, <see cref="SpriteAtlasPackingSettings"/>,
    /// <see cref="SpriteAtlasTextureSettings"/>, <see cref="variantScale"/> and
    /// <see cref="includeInBuild"/> in the asset's `.meta` (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/spriteatlaseditor v2importerplatform, v2importerplatformset,
    /// v2importerpacking, v2importertexture, v2importervariantscale and v2importerinclude).
    /// </summary>
    [HelpURL("https://docs.unity3d.com/6000.0/Documentation/Manual/sprite/atlas/v2/sprite-atlas-v2.html")]
    public sealed partial class SpriteAtlasImporter : AssetImporter
    {
        SpriteAtlasEditorSettings m_Settings = new SpriteAtlasEditorSettings();

        public SpriteAtlasImporter() { }

        internal override void ReadSettings(YNode body)
        {
            m_Settings = SpriteAtlasEditorSettings.Read(body, prefixed: false);
        }

        void SettingsChanged()
        {
            EditorUtility.SetDirty(this);
            Changed();
        }

        /// <summary>This importer's <c>variantMultiplier</c> from its `.meta` (defaults to 1 when
        /// omitted; setting stores the value verbatim without clamping and marks this importer dirty
        /// only when <c>m_VariantScale != value</c>, so same finite values and <c>-0f</c> on
        /// <c>0f</c> are no-ops while <c>NaN</c> always dirties; matches the Unity 6000.0.71f1
        /// Editor, tests/projects/spriteatlaseditor v2importervariantscale).</summary>
        public float variantScale
        {
            get
            {
                CheckImporterState("get_variantScale");
                return m_Settings.VariantScale;
            }
            set
            {
                CheckImporterState("set_variantScale");
                if (m_Settings.VariantScale == value) return;
                m_Settings.VariantScale = value;
                SettingsChanged();
            }
        }

        /// <summary>Whether builds include this sprite atlas (<c>bindAsDefault</c> in its `.meta`,
        /// defaulting to true when omitted; setting marks this importer dirty only when the value
        /// changes; matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor
        /// v2importerinclude).</summary>
        public bool includeInBuild
        {
            get
            {
                CheckImporterState("get_includeInBuild");
                return m_Settings.IncludeInBuild;
            }
            set
            {
                CheckImporterState("set_includeInBuild");
                if (m_Settings.IncludeInBuild == value) return;
                m_Settings.IncludeInBuild = value;
                SettingsChanged();
            }
        }

        /// <summary>This importer's own <see cref="SpriteAtlasTextureSettings"/> from its `.meta`
        /// (defaults when omitted, legacy v1 <c>colorSpace</c> mapped to <c>sRGB</c>, variant
        /// independent of its master; setting stores all fields verbatim and unconditionally marks
        /// this importer dirty; matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/spriteatlaseditor v2importertexture).</summary>
        public SpriteAtlasTextureSettings textureSettings
        {
            get
            {
                CheckImporterState("get_textureSettings");
                return m_Settings.Texture;
            }
            set
            {
                CheckImporterState("set_textureSettings");
                m_Settings.Texture = value;
                SettingsChanged();
            }
        }

        /// <summary>This importer's own <see cref="SpriteAtlasPackingSettings"/> from its `.meta`
        /// (a variant importer reads and writes its own `.meta` independently of its master, while
        /// the imported variant <see cref="SpriteAtlas"/> inherits its master's packing settings;
        /// setting stores all fields verbatim and unconditionally marks this importer dirty;
        /// matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor
        /// v2importerpacking).</summary>
        public SpriteAtlasPackingSettings packingSettings
        {
            get
            {
                CheckImporterState("get_packingSettings");
                return m_Settings.Packing;
            }
            set
            {
                CheckImporterState("set_packingSettings");
                m_Settings.Packing = value;
                SettingsChanged();
            }
        }

        /// <summary>Returns a fresh copy of this importer's platform settings for
        /// <paramref name="buildTarget"/> (after normalizing the build target name), or fresh
        /// Editor defaults named after the normalized target when absent.</summary>
        public TextureImporterPlatformSettings GetPlatformSettings(string buildTarget)
        {
            buildTarget = TextureImporter.GetTexturePlatformSerializationName(buildTarget);
            CheckImporterState("GetPlatformSettings_Internal");
            return m_Settings.PlatformSettings(buildTarget);
        }

        /// <summary>Normalizes <paramref name="src"/>.<see cref="TextureImporterPlatformSettings.name"/>
        /// in place, replaces any matching platform entry in place or appends a defensive copy when
        /// overridden or named <c>DefaultTexturePlatform</c>, and unconditionally marks this importer
        /// dirty (matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor
        /// v2importerplatformset).</summary>
        public void SetPlatformSettings(TextureImporterPlatformSettings src)
        {
            src.name = TextureImporter.GetTexturePlatformSerializationName(src.name);
            CheckImporterState("SetPlatformSettings_Internal");
            m_Settings.SetPlatformSettings(src);
            SettingsChanged();
        }

        internal override bool UsesDirtyFlagForWrite => true;

        /// <summary>Writes the settings whole into the `.meta` written for a change, as the Editor
        /// writes the whole importer (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/spriteatlaseditor v2wholewrite Legacy and UserData).</summary>
        internal override bool WriteCustomMeta(YText section)
        {
            m_Settings.Write(section, prefixed: false);
            return true;
        }

        /// <summary>Evicts the cached importer, reloads this V2 sprite atlas from disk, and
        /// reimports any loaded V2 variant whose master references <paramref name="guid"/>
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor
        /// v2importerpacking).</summary>
        internal static void ReimportAsset(string path, string guid)
        {
            bool outer = SpriteAtlas.s_Reimporting;
            SpriteAtlas.s_Reimporting = true;
            try { Reimport(path, guid); }
            finally { SpriteAtlas.s_Reimporting = outer; }
        }

        static void Reimport(string path, string guid)
        {
            AssetImporter.Evict(path);
            AssetDatabaseRuntime.Invalidate(guid);
            AssetDatabaseRuntime.LoadFile(guid);
            var dependentGuids = new List<string>();
            foreach (var obj in ObjectRegistry.All(typeof(SpriteAtlas)))
            {
                if (obj is SpriteAtlas variant && variant.IsFromImporter && variant.isVariant
                    && string.Equals(variant.MasterAtlasRef.Guid, guid, StringComparison.OrdinalIgnoreCase)
                    && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(variant, out string varGuid, out _)
                    && !string.IsNullOrEmpty(varGuid)
                    && !string.Equals(varGuid, guid, StringComparison.OrdinalIgnoreCase))
                {
                    dependentGuids.Add(varGuid);
                }
            }
            foreach (var varGuid in dependentGuids)
            {
                AssetDatabaseRuntime.Invalidate(varGuid);
                AssetDatabaseRuntime.LoadFile(varGuid);
            }
        }
    }
}
