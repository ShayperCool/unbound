using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D
{
    /// <summary>Base class for custom sprite atlas packers referenced by
    /// <see cref="SpriteAtlasAsset.SetScriptablePacker"/>.</summary>
    public abstract partial class ScriptablePacker : ScriptableObject
    {
        protected ScriptablePacker() { }
    }

    /// <summary>
    /// Source asset representation of a V2 sprite atlas (<c>.spriteatlasv2</c>), read and written
    /// directly via serialized files independently of <see cref="AssetDatabase"/> import
    /// (matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlasasset).
    /// </summary>
    public partial class SpriteAtlasAsset : Object
    {
        internal const int ClassId = 612988286;

        readonly List<SpriteAtlas.PackableEntry> m_Packables = new List<SpriteAtlas.PackableEntry>();
        SpriteAtlas m_MasterAtlas;
        ObjectRef m_MasterRef;
        bool m_IsVariant;
        Object m_ScriptablePacker;
        ObjectRef m_ScriptablePackerRef;

        public SpriteAtlasAsset()
        {
            EditorUtility.SetDirty(this);
        }

        internal SpriteAtlasAsset(bool fromDeserialization)
        {
            _ = fromDeserialization;
        }

        public bool isVariant => m_IsVariant;

        public void SetIsVariant(bool value)
        {
            if (m_IsVariant == value) return;
            m_IsVariant = value;
            EditorUtility.SetDirty(this);
        }

        public void SetMasterAtlas(SpriteAtlas atlas)
        {
            m_MasterAtlas = atlas != null ? atlas : null;
            m_MasterRef = default;
        }

        public SpriteAtlas GetMasterAtlas()
        {
            if (m_MasterAtlas != null) return m_MasterAtlas;
            if (!string.IsNullOrEmpty(m_MasterRef.Guid) && m_MasterRef.FileId != 0)
            {
                m_MasterAtlas = AssetDatabaseRuntime.Load(m_MasterRef.Guid, m_MasterRef.FileId, typeof(SpriteAtlas)) as SpriteAtlas;
                return m_MasterAtlas;
            }
            return null;
        }

        internal Object GetPacker()
        {
            if (m_ScriptablePacker != null) return m_ScriptablePacker;
            if (!string.IsNullOrEmpty(m_ScriptablePackerRef.Guid) && m_ScriptablePackerRef.FileId != 0)
            {
                m_ScriptablePacker = AssetDatabaseRuntime.Load(m_ScriptablePackerRef.Guid, m_ScriptablePackerRef.FileId, typeof(Object));
                return m_ScriptablePacker;
            }
            return null;
        }

        internal void SetPacker(Object obj)
        {
            if (obj == null)
            {
                m_ScriptablePacker = null;
                m_ScriptablePackerRef = default;
                EditorUtility.SetDirty(this);
                return;
            }
            if (obj is ScriptablePacker)
            {
                m_ScriptablePacker = obj;
                m_ScriptablePackerRef = default;
                EditorUtility.SetDirty(this);
                return;
            }
            Debug.LogWarning("Attempted to set invalid scriptableobject as UnityEditor.U2D.ScriptablePacker.");
        }

        public void SetScriptablePacker(ScriptablePacker obj) => SetPacker(obj);

        public void Add(Object[] objects)
        {
            SpriteAtlas.AddPackableItems(m_Packables, objects, "Sprite Atlas Importer");
            EditorUtility.SetDirty(this);
        }

        public void Remove(Object[] objects)
        {
            SpriteAtlas.RemovePackableItems(m_Packables, objects, this);
            EditorUtility.SetDirty(this);
        }

        internal void RemoveAt(int index)
        {
            if ((uint)index >= (uint)m_Packables.Count) return;
            int last = m_Packables.Count - 1;
            m_Packables[index] = m_Packables[last];
            m_Packables.RemoveAt(last);
            EditorUtility.SetDirty(this);
        }

        [Obsolete("SetVariantScale is no longer supported and will be removed. Use SpriteAtlasImporter.SetVariantScale instead.")]
        [Unbound.EditorFaithful("obsolete no-op in Unity 6000.0.71f1 (tests/projects/spriteatlasasset)")]
        public void SetVariantScale(float value) { }

        [Obsolete("SetIncludeInBuild is no longer supported and will be removed. Use SpriteAtlasImporter.SetIncludeInBuild instead.")]
        [Unbound.EditorFaithful("obsolete no-op in Unity 6000.0.71f1 (tests/projects/spriteatlasasset)")]
        public void SetIncludeInBuild(bool value) { }

        [Obsolete("IsIncludeInBuild is no longer supported and will be removed. Use SpriteAtlasImporter.IsIncludeInBuild instead.")]
        [Unbound.EditorFaithful("obsolete method always returns true in Unity 6000.0.71f1 (tests/projects/spriteatlasasset)")]
        public bool IsIncludeInBuild() => true;

        [Obsolete("SetPlatformSettings is no longer supported and will be removed. Use SpriteAtlasImporter.SetPlatformSettings instead.")]
        [Unbound.EditorFaithful("obsolete no-op in Unity 6000.0.71f1 (tests/projects/spriteatlasasset)")]
        public void SetPlatformSettings(TextureImporterPlatformSettings src) { }

        [Obsolete("SetTextureSettings is no longer supported and will be removed. Use SpriteAtlasImporter.SetTextureSettings instead.")]
        [Unbound.EditorFaithful("obsolete no-op in Unity 6000.0.71f1 (tests/projects/spriteatlasasset)")]
        public void SetTextureSettings(SpriteAtlasTextureSettings src) { }

        [Obsolete("SetPackingSettings is no longer supported and will be removed. Use SpriteAtlasImporter.SetPackingSettings instead.")]
        [Unbound.EditorFaithful("obsolete no-op in Unity 6000.0.71f1 (tests/projects/spriteatlasasset)")]
        public void SetPackingSettings(SpriteAtlasPackingSettings src) { }

        [Obsolete("GetPackingSettings is no longer supported and will be removed. Use SpriteAtlasImporter.GetPackingSettings instead.")]
        [Unbound.EditorFaithful("obsolete method always returns default in Unity 6000.0.71f1 (tests/projects/spriteatlasasset)")]
        public SpriteAtlasPackingSettings GetPackingSettings() => default;

        [Obsolete("GetTextureSettings is no longer supported and will be removed. Use SpriteAtlasImporter.GetTextureSettings instead.")]
        [Unbound.EditorFaithful("obsolete method always returns default in Unity 6000.0.71f1 (tests/projects/spriteatlasasset)")]
        public SpriteAtlasTextureSettings GetTextureSettings() => default;

        [Obsolete("GetPlatformSettings is no longer supported and will be removed. Use SpriteAtlasImporter.GetPlatformSettingss instead.")]
        public TextureImporterPlatformSettings GetPlatformSettings(string buildTarget) => new TextureImporterPlatformSettings();

        public static SpriteAtlasAsset Load(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            var objs = InternalEditorUtility.LoadSerializedFileAndForget(assetPath);
            return objs.Length > 0 ? objs[0] as SpriteAtlasAsset : null;
        }

        public static void Save(SpriteAtlasAsset asset, string assetPath)
        {
            if (asset == null) throw new ArgumentNullException("Parameter asset is null");
            InternalEditorUtility.SaveToSerializedFileAndForget(new Object[] { asset }, assetPath, true);
        }

        internal void ReadSerialized(YNode body)
        {
            if (body == null || body.Kind != YKind.Map) return;
            hideFlags = (HideFlags)body.Int("m_ObjectHideFlags");
            m_Name = body.Str("m_Name", "");
            m_MasterRef = body.Ref("m_MasterAtlas");
            m_MasterAtlas = null;
            m_Packables.Clear();
            var importerData = body.Get("m_ImporterData");
            foreach (var item in importerData?.Get("packables")?.Seq() ?? Array.Empty<YNode>())
                m_Packables.Add(new SpriteAtlas.PackableEntry(item.AsRef(), null));
            m_IsVariant = body.Bool("m_IsVariant");
            m_ScriptablePackerRef = body.Ref("m_ScriptablePacker");
            m_ScriptablePacker = null;
        }

        internal void WriteYaml(UnityYamlWriter.Writer writer)
        {
            writer.Int("m_ObjectHideFlags", (uint)HideFlagsNoThrow);
            writer.Line(1, "m_CorrespondingSourceObject", "{fileID: 0}");
            writer.Line(1, "m_PrefabInstance", "{fileID: 0}");
            writer.Line(1, "m_PrefabAsset", "{fileID: 0}");
            writer.String("m_Name", m_Name ?? "");
            writer.Int("serializedVersion", 2);

            var masterRef = m_MasterAtlas != null
                ? SpriteAtlas.ResolveMasterRef(m_MasterAtlas)
                : ReferenceEquals(m_MasterAtlas, null) ? m_MasterRef : default;
            writer.Line(1, "m_MasterAtlas", UnityYamlWriter.Writer.ReferenceText(masterRef));

            writer.Key("m_ImporterData", 1);
            if (m_Packables.Count == 0)
            {
                writer.Line(2, "packables", "[]");
            }
            else
            {
                writer.Key("packables", 2);
                foreach (var entry in m_Packables)
                    writer.SequenceValue(2, UnityYamlWriter.Writer.ReferenceText(entry.CurrentRef));
            }

            writer.Bool("m_IsVariant", m_IsVariant);

            string packerText;
            if (m_ScriptablePacker != null)
                packerText = writer.ReferenceText(m_ScriptablePacker);
            else if (ReferenceEquals(m_ScriptablePacker, null) && !m_ScriptablePackerRef.IsNull)
                packerText = UnityYamlWriter.Writer.ReferenceText(m_ScriptablePackerRef);
            else
                packerText = "{fileID: 0}";
            writer.Line(1, "m_ScriptablePacker", packerText);
        }
    }
}
