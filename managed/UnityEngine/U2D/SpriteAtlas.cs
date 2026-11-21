using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.U2D;
using Object = UnityEngine.Object;

namespace UnityEngine.U2D
{
    /// <summary>
    /// A sprite atlas: its sprites are those of its packables (sprites, textures and folders; a
    /// variant's are its master's). Read from a `.spriteatlas` (SpriteAtlas, its `m_EditorData`)
    /// or a `.spriteatlasv2` (SpriteAtlasAsset, its `m_ImporterData`). When the project's Sprite
    /// Packer mode packs it for Play mode (see SpriteAtlasPacking), GetSprite and GetSprites give
    /// clones on its pages, in its packed order (by texture GUID; on one texture by name hash);
    /// otherwise clones of the unpacked sprites in name order (unmeasured outside Play mode).
    /// Measured on the 6000.0 Editor: the name is the file's name, the tag V1's `m_Tag` (a
    /// variant's is its master's name), GetSprite returns a new clone named `name(Clone)` (the
    /// exact name, else null) (tests/projects/spriteatlas).
    /// </summary>
    [ClassId(687078895)]
    [ClassId(612988286)]
    public partial class SpriteAtlas : Object
    {
        /// <summary>The main object's fileID in a `.spriteatlasv2` (made by its importer; the file's
        /// own document is &amp;1).</summary>
        internal const long ImportedFileId = 100100200;

        /// <summary>A packable entry: a serialized reference as read from disk, plus the live
        /// object added via <see cref="SpriteAtlasExtensions.Add"/> when present.</summary>
        internal readonly struct PackableEntry
        {
            internal readonly ObjectRef Reference;
            internal readonly Object Live;

            internal PackableEntry(ObjectRef reference, Object live)
            {
                Reference = reference;
                Live = live;
            }
        }

        readonly List<PackableEntry> m_Packables = new List<PackableEntry>();
        bool m_PackablesModified;
        bool m_IsVariant;
        // Editor settings ("Include in Build", packingSettings, textureSettings, platformSettings):
        // a V1 atlas reads them from its editor data; a V2 atlas reads them from its
        // SpriteAtlasImporter .meta when first asked (m_ImporterSettingsRead).
        bool m_IncludeInBuild = true;
        SpriteAtlasPackingSettings m_PackingSettings = SpriteAtlasPackingSettings.Default;
        bool m_PackingSettingsModified;
        SpriteAtlasTextureSettings m_TextureSettings = SpriteAtlasTextureSettings.Default;
        bool m_TextureSettingsModified;
        List<TextureImporterPlatformSettings> m_PlatformSettings;
        bool m_PlatformSettingsModified;
        bool m_FromImporter;
        bool m_ImporterSettingsRead = true;
        SpriteAtlas m_Master;
        string m_Tag;
        List<Sprite> m_Sprites;

        public SpriteAtlas()
        {
            EditorUtility.SetDirty(this);
        }

        public bool isVariant => m_IsVariant;

        public string tag => string.IsNullOrEmpty(m_Tag) ? name : m_Tag;

        public int spriteCount => Sprites().Count;

        public bool CanBindTo(Sprite sprite) => sprite != null && Sprites().Contains(sprite);

        public Sprite GetSprite(string name)
        {
            foreach (var sprite in Sprites())
                if (sprite.name == name) return SpriteAtlasPacking.Clone(this, sprite);
            return null;
        }

        public int GetSprites(Sprite[] sprites) => GetSprites(sprites, null);

        public int GetSprites(Sprite[] sprites, string name)
        {
            if (sprites == null) throw new ArgumentNullException(nameof(sprites));
            int count = 0;
            foreach (var sprite in Sprites())
            {
                if (count >= sprites.Length) break;
                if (name != null && sprite.name != name) continue;
                sprites[count++] = SpriteAtlasPacking.Clone(this, sprite);
            }
            return count;
        }

        List<Sprite> Sprites()
        {
            var packed = SpriteAtlasPacking.PackedSprites(this);
            if (packed != null) return packed;
            if (m_Sprites != null) return m_Sprites;
            if (m_IsVariant && m_Master != null && !ReferenceEquals(m_Master, this)) return m_Sprites = m_Master.Sprites();
            var found = new List<Sprite>();
            foreach (var entry in m_Packables)
            {
                if (!ReferenceEquals(entry.Live, null))
                {
                    if (entry.Live == null) continue;
                    if (entry.Live is Sprite liveSprite)
                    {
                        found.Add(liveSprite);
                        continue;
                    }
                }
                var packable = entry.Reference;
                if (packable.Guid == null) continue;
                var path = AssetDatabase.GUIDToAssetPath(packable.Guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { path }))
                        found.AddRange(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)).OfType<Sprite>());
                }
                else if (AssetDatabase.LoadAssetAtPath(path, typeof(Object)) is Texture2D)
                {
                    // A texture: all its sprites; a sprite of it, that one.
                    var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>();
                    if (packable.FileId != 2800000)
                        sprites = sprites.Where(s => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(s, out _, out long id) && id == packable.FileId);
                    found.AddRange(sprites);
                }
            }
            return m_Sprites = found.Distinct().OrderBy(s => s.name, StringComparer.Ordinal).ToList();
        }

        /// <summary>The packables as stored, in order.</summary>
        internal IReadOnlyList<PackableEntry> Packables => m_Packables;

        /// <summary>Whether Add has updated this atlas's packables in memory.</summary>
        internal bool PackablesModified => m_PackablesModified;

        /// <summary>Appends valid packable objects (Texture2D, Sprite, or folder DefaultAsset) in
        /// input order without deduplication, logs warnings for null or invalid entries, and
        /// unconditionally marks the atlas dirty (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/spriteatlaseditor v1add/v2add).</summary>
        internal void AddPackables(Object[] objects)
        {
            bool added = false;
            if (objects != null)
            {
                for (int i = 0; i < objects.Length; i++)
                {
                    var obj = objects[i];
                    if (obj == null)
                    {
                        Debug.LogWarning("Attempted to add null object into a Sprite Atlas.");
                        continue;
                    }
                    bool valid = obj is Texture2D || obj is Sprite;
                    if (!valid && obj is DefaultAsset)
                    {
                        var path = AssetDatabase.GetAssetPath(obj);
                        valid = !string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path);
                    }
                    if (!valid)
                    {
                        Debug.LogWarning($"Attempted to add invalid object into a Sprite Atlas. {obj.name} was a {obj.GetType().Name}. Only Folder, Texture and Sprite are valid.");
                        continue;
                    }
                    m_Packables.Add(new PackableEntry(ResolvePackableRef(obj), obj));
                    added = true;
                }
            }
            if (added) m_Sprites = null;
            m_PackablesModified = true;
            EditorUtility.SetDirty(this);
        }

        static ObjectRef ResolvePackableRef(Object obj)
        {
            if (obj != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId) && !string.IsNullOrEmpty(guid))
                return new ObjectRef(localId, guid, AssetDatabase.IsNativeAsset(obj) ? 2 : 3);
            return default;
        }

        internal YText ToPackablesYText()
        {
            var seq = YText.Seq(flow: m_Packables.Count == 0);
            for (int i = 0; i < m_Packables.Count; i++)
            {
                var entry = m_Packables[i];
                var reference = ReferenceEquals(entry.Live, null)
                    ? entry.Reference
                    : entry.Live == null ? default : ResolvePackableRef(entry.Live);
                seq.Items.Add(SceneFiles.FlowValue(UnityYamlWriter.Writer.ReferenceText(reference)));
            }
            return seq;
        }

        /// <summary>The master atlas of a variant (null for an atlas without one).</summary>
        internal SpriteAtlas MasterAtlas => m_Master;

        /// <summary>Whether the atlas was imported from a V2 `.spriteatlasv2` asset rather than a
        /// V1 native `.spriteatlas` file.</summary>
        internal bool IsFromImporter => m_FromImporter;

        /// <summary>Whether builds include the atlas ("Include in Build").</summary>
        internal bool IncludeInBuild
        {
            get
            {
                EnsureImporterSettingsRead();
                return m_IncludeInBuild;
            }
        }

        /// <summary>Sets whether builds include the atlas, marking it dirty only when the value
        /// changes (matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor).</summary>
        internal void WriteIncludeInBuild(bool value)
        {
            EnsureImporterSettingsRead();
            if (m_IncludeInBuild == value) return;
            m_IncludeInBuild = value;
            EditorUtility.SetDirty(this);
        }

        /// <summary>The atlas's packing settings (a V2 variant copies its non-variant master's
        /// settings on import, and subsequent SetPackingSettings calls update the atlas itself).</summary>
        internal SpriteAtlasPackingSettings PackingSettings
        {
            get
            {
                EnsureImporterSettingsRead();
                return m_PackingSettings;
            }
        }

        /// <summary>Whether SetPackingSettings has updated this atlas's packing settings in memory.</summary>
        internal bool PackingSettingsModified => m_PackingSettingsModified;

        /// <summary>Replaces the atlas's packing settings verbatim and marks it dirty (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor v1packing/v2packing).</summary>
        internal void WritePackingSettings(SpriteAtlasPackingSettings src)
        {
            EnsureImporterSettingsRead();
            m_PackingSettings = src;
            m_PackingSettingsModified = true;
            EditorUtility.SetDirty(this);
        }

        /// <summary>The atlas's texture settings.</summary>
        internal SpriteAtlasTextureSettings TextureSettings
        {
            get
            {
                EnsureImporterSettingsRead();
                return m_TextureSettings;
            }
        }

        /// <summary>Whether SetTextureSettings has updated this atlas's texture settings in memory.</summary>
        internal bool TextureSettingsModified => m_TextureSettingsModified;

        /// <summary>Replaces the atlas's texture settings verbatim and marks it dirty (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor v1texture/v2texture).</summary>
        internal void WriteTextureSettings(SpriteAtlasTextureSettings src)
        {
            EnsureImporterSettingsRead();
            m_TextureSettings = src;
            m_TextureSettingsModified = true;
            EditorUtility.SetDirty(this);
        }

        /// <summary>A fresh copy of the atlas's platform settings for <paramref name="buildTarget"/>,
        /// or fresh Editor defaults named after the normalized target when no entry matches.</summary>
        internal TextureImporterPlatformSettings ReadPlatformSettings(string buildTarget)
        {
            EnsureImporterSettingsRead();
            return TextureImporterPlatformSettings.FindOrDefault(m_PlatformSettings, buildTarget);
        }

        /// <summary>The atlas's stored platform settings list, if any.</summary>
        internal List<TextureImporterPlatformSettings> PlatformSettings => m_PlatformSettings;

        /// <summary>Whether SetPlatformSettings has updated this atlas's platform settings in memory.</summary>
        internal bool PlatformSettingsModified => m_PlatformSettingsModified;

        /// <summary>Replaces a matching platform entry in place, appends a new entry when overridden
        /// or named DefaultTexturePlatform, and unconditionally marks the atlas dirty (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor v1platformset/v2platformset).</summary>
        internal void WritePlatformSettings(TextureImporterPlatformSettings src)
        {
            EnsureImporterSettingsRead();
            TextureImporterPlatformSettings.SetOrAdd(ref m_PlatformSettings, src);
            m_PlatformSettingsModified = true;
            EditorUtility.SetDirty(this);
        }

        void EnsureImporterSettingsRead()
        {
            if (m_ImporterSettingsRead) return;
            m_ImporterSettingsRead = true;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(this, out string guid, out long _) && !string.IsNullOrEmpty(guid))
            {
                try
                {
                    var docs = Native.LoadDocuments("meta:" + guid);
                    if (docs.Count > 0 && docs[0].TypeName == "SpriteAtlasImporter")
                    {
                        var importer = docs[0].Body;
                        m_IncludeInBuild = importer.Bool("bindAsDefault", true);
                        m_PackingSettings = m_IsVariant && m_Master != null && !ReferenceEquals(m_Master, this) && !m_Master.m_IsVariant
                            ? m_Master.PackingSettings
                            : SpriteAtlasPackingSettings.Read(importer.Get("packingSettings"));
                        m_TextureSettings = SpriteAtlasTextureSettings.Read(importer.Get("textureSettings"));
                        m_PlatformSettings = TextureImporterPlatformSettings.ReadList(importer.Get("platformSettings"), prefixed: false);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Unbound: the import settings of sprite atlas {name} could not be read ({e.Message}); it reports default settings");
                }
            }
        }

        internal void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Packables.Clear();
            var data = body.Get("m_ImporterData") ?? body.Get("m_EditorData");
            foreach (var packable in data?.Get("packables")?.Seq() ?? Array.Empty<YNode>())
                m_Packables.Add(new PackableEntry(packable.AsRef(), null));
            m_PackablesModified = false;
            m_FromImporter = body.Get("m_ImporterData") != null;
            m_PackingSettingsModified = false;
            m_TextureSettingsModified = false;
            m_PlatformSettingsModified = false;
            if (m_FromImporter)
            {
                m_IncludeInBuild = true;
                m_PackingSettings = SpriteAtlasPackingSettings.Default;
                m_TextureSettings = SpriteAtlasTextureSettings.Default;
                m_PlatformSettings = null;
                m_ImporterSettingsRead = false;
            }
            else
            {
                m_IncludeInBuild = data?.Bool("bindAsDefault", true) ?? true;
                m_PackingSettings = SpriteAtlasPackingSettings.Read(data?.Get("packingSettings"));
                m_TextureSettings = SpriteAtlasTextureSettings.Read(data?.Get("textureSettings"));
                m_PlatformSettings = TextureImporterPlatformSettings.ReadList(data?.Get("platformSettings"), prefixed: true);
                m_ImporterSettingsRead = true;
            }
            m_IsVariant = body.Bool("m_IsVariant");
            m_Tag = body.Str("m_Tag", null);
            var master = body.Ref("m_MasterAtlas");
            m_Master = master.FileId != 0 ? resolver.Resolve(master, typeof(SpriteAtlas)) as SpriteAtlas : null;
            m_Sprites = null;
            EditorUtility.ClearDirty(this);
        }
    }
}
