using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using Object = UnityEngine.Object;

namespace UnityEngine.U2D
{
    /// <summary>
    /// A sprite atlas, as the Editor has it in Play mode: its sprites are those of its packables
    /// (sprites, textures and folders; a variant's are its master's), and they keep their own
    /// textures (Unbound does not pack them; they draw the same). Read from a `.spriteatlas`
    /// (SpriteAtlas, its `m_EditorData`) or a `.spriteatlasv2` (SpriteAtlasAsset, its
    /// `m_ImporterData`). Measured on the 6000.0 Editor: the name and tag are the file's name,
    /// GetSprite returns a new clone named `name(Clone)` (the exact name, else null), GetSprites
    /// gives clones in name order.
    /// </summary>
    [ClassId(687078895)]
    [ClassId(612988286)]
    public partial class SpriteAtlas : Object, ISerializedAsset
    {
        /// <summary>The main object's fileID in a `.spriteatlasv2` (made by its importer; the file's
        /// own document is &amp;1).</summary>
        internal const long ImportedFileId = 100100200;

        readonly List<ObjectRef> m_Packables = new List<ObjectRef>();
        bool m_IsVariant;
        SpriteAtlas m_Master;
        List<Sprite> m_Sprites;

        public SpriteAtlas() { }

        public bool isVariant => m_IsVariant;

        public string tag => name;

        public int spriteCount => Sprites().Count;

        public bool CanBindTo(Sprite sprite) => sprite != null && Sprites().Contains(sprite);

        public Sprite GetSprite(string name)
        {
            foreach (var sprite in Sprites())
                if (sprite.name == name) return (Sprite)Instantiate(sprite);
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
                sprites[count++] = (Sprite)Instantiate(sprite);
            }
            return count;
        }

        List<Sprite> Sprites()
        {
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

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Packables.Clear();
            var data = body.Get("m_ImporterData") ?? body.Get("m_EditorData");
            foreach (var packable in data?.Get("packables")?.Seq() ?? Array.Empty<YNode>())
                m_Packables.Add(packable.AsRef());
            m_IsVariant = body.Bool("m_IsVariant");
            var master = body.Ref("m_MasterAtlas");
            m_Master = master.FileId != 0 ? resolver.Resolve(master, typeof(SpriteAtlas)) as SpriteAtlas : null;
            m_Sprites = null;
        }
    }
}
