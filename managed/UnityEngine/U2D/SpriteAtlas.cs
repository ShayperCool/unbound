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

        readonly List<ObjectRef> m_Packables = new List<ObjectRef>();
        bool m_IsVariant;
        // Editor settings ("Include in Build", packingSettings, textureSettings): a V1 atlas reads
        // them from its editor data; a V2 atlas reads them from its SpriteAtlasImporter .meta when
        // first asked (m_ImporterSettingsRead).
        bool m_IncludeInBuild = true;
        SpriteAtlasPackingSettings m_PackingSettings = SpriteAtlasPackingSettings.Default;
        SpriteAtlasTextureSettings m_TextureSettings = SpriteAtlasTextureSettings.Default;
        bool m_FromImporter;
        bool m_ImporterSettingsRead = true;
        SpriteAtlas m_Master;
        string m_Tag;
        List<Sprite> m_Sprites;

        public SpriteAtlas() { }

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
            foreach (var packable in m_Packables)
            {
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
        internal IReadOnlyList<ObjectRef> Packables => m_Packables;

        /// <summary>The master atlas of a variant (null for an atlas without one).</summary>
        internal SpriteAtlas MasterAtlas => m_Master;

        /// <summary>Whether builds include the atlas ("Include in Build").</summary>
        internal bool IncludeInBuild
        {
            get
            {
                EnsureImporterSettingsRead();
                return m_IncludeInBuild;
            }
        }

        /// <summary>The atlas's packing settings (for a V2 variant with a non-variant master, its master's).</summary>
        internal SpriteAtlasPackingSettings PackingSettings
        {
            get
            {
                EnsureImporterSettingsRead();
                if (m_FromImporter && m_IsVariant && m_Master != null && !ReferenceEquals(m_Master, this) && !m_Master.m_IsVariant)
                {
                    m_Master.EnsureImporterSettingsRead();
                    return m_Master.m_PackingSettings;
                }
                return m_PackingSettings;
            }
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
                        m_PackingSettings = SpriteAtlasPackingSettings.Read(importer.Get("packingSettings"));
                        m_TextureSettings = SpriteAtlasTextureSettings.Read(importer.Get("textureSettings"));
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
                m_Packables.Add(packable.AsRef());
            m_FromImporter = body.Get("m_ImporterData") != null;
            if (m_FromImporter)
            {
                m_IncludeInBuild = true;
                m_PackingSettings = SpriteAtlasPackingSettings.Default;
                m_TextureSettings = SpriteAtlasTextureSettings.Default;
                m_ImporterSettingsRead = false;
            }
            else
            {
                m_IncludeInBuild = data?.Bool("bindAsDefault", true) ?? true;
                m_PackingSettings = SpriteAtlasPackingSettings.Read(data?.Get("packingSettings"));
                m_TextureSettings = SpriteAtlasTextureSettings.Read(data?.Get("textureSettings"));
                m_ImporterSettingsRead = true;
            }
            m_IsVariant = body.Bool("m_IsVariant");
            m_Tag = body.Str("m_Tag", null);
            var master = body.Ref("m_MasterAtlas");
            m_Master = master.FileId != 0 ? resolver.Resolve(master, typeof(SpriteAtlas)) as SpriteAtlas : null;
            m_Sprites = null;
        }
    }
}
