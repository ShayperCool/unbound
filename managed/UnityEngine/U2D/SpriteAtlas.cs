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

            /// <summary>The reference written for the entry: as read, else the live object's
            /// (none once it is destroyed or when it is not an asset).</summary>
            internal ObjectRef CurrentRef => ReferenceEquals(Live, null)
                ? Reference
                : Live == null ? default : ResolvePackableRef(Live);
        }

        /// <summary>Minimum and maximum variant scale clamped by
        /// <see cref="SpriteAtlasExtensions.SetVariantScale"/> (matches Unity 6000.0.71f1 Editor,
        /// tests/projects/spriteatlaseditor v1variantscale/v2variantscale).</summary>
        internal const float MinVariantScale = 0.1f;
        internal const float MaxVariantScale = 1f;

        readonly List<PackableEntry> m_Packables = new List<PackableEntry>();
        bool m_PackablesModified;
        bool m_IsVariant;
        bool m_IsVariantModified;
        // The Editor settings: a V1 atlas reads them from its editor data; a V2 atlas from its
        // SpriteAtlasImporter .meta when first asked (m_ImporterSettingsRead).
        SpriteAtlasEditorSettings m_Settings = new SpriteAtlasEditorSettings();
        /// <summary>A V2 variant's packing settings: its master's when the variant was imported.</summary>
        SpriteAtlasPackingSettings? m_MasterPackingAtImport;
        bool m_FromImporter;
        bool m_ImporterSettingsRead = true;
        ObjectRef m_MasterRef;
        SpriteAtlas m_Master;
        bool m_MasterAtlasModified;
        string m_Tag;
        bool m_TagModified;
        readonly List<ObjectRef> m_SerializedPackedSprites = new List<ObjectRef>();
        readonly List<string> m_SerializedPackedSpriteNames = new List<string>();
        bool m_PackedDataModified;
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
            if (!IsPersistentAsset || m_SerializedPackedSprites.Count == 0)
                return s_EmptySprites;
            var found = new List<Sprite>(m_SerializedPackedSprites.Count);
            foreach (var r in m_SerializedPackedSprites)
            {
                if (string.IsNullOrEmpty(r.Guid) || r.FileId == 0) continue;
                if (AssetDatabaseRuntime.Load(r.Guid, r.FileId, typeof(Sprite)) is Sprite s)
                    found.Add(s);
            }
            return m_Sprites = found;
        }

        static readonly List<Sprite> s_EmptySprites = new List<Sprite>();

        internal IReadOnlyList<ObjectRef> SerializedPackedSprites => m_SerializedPackedSprites;
        internal bool PackedDataModified => m_PackedDataModified;

        internal void ApplyPackedSprites(List<ObjectRef> refs, List<string> names, bool markModified)
        {
            m_SerializedPackedSprites.Clear();
            if (refs != null) m_SerializedPackedSprites.AddRange(refs);
            m_SerializedPackedSpriteNames.Clear();
            if (names != null) m_SerializedPackedSpriteNames.AddRange(names);
            if (markModified) m_PackedDataModified = true;
            m_Sprites = null;
        }

        internal void ClearPackedDataModified() => m_PackedDataModified = false;

        internal void InvalidateCachedSprites() => m_Sprites = null;

        internal YText ToPackedRenderDataKeysYText()
        {
            var seq = YText.Seq(flow: m_SerializedPackedSprites.Count == 0);
            for (int i = 0; i < m_SerializedPackedSprites.Count; i++)
            {
                var r = m_SerializedPackedSprites[i];
                var entry = YText.Map();
                entry.Set(r.Guid ?? "", YText.Scalar(r.FileId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                seq.Items.Add(entry);
            }
            return seq;
        }

        internal YText ToPackedSpritesYText()
        {
            var seq = YText.Seq(flow: m_SerializedPackedSprites.Count == 0);
            for (int i = 0; i < m_SerializedPackedSprites.Count; i++)
                seq.Items.Add(SceneFiles.FlowValue(UnityYamlWriter.Writer.ReferenceText(m_SerializedPackedSprites[i])));
            return seq;
        }

        internal YText ToPackedSpriteNamesYText()
        {
            var seq = YText.Seq(flow: m_SerializedPackedSpriteNames.Count == 0);
            for (int i = 0; i < m_SerializedPackedSpriteNames.Count; i++)
                seq.Items.Add(YText.Scalar(UnityYamlWriter.Scalar(m_SerializedPackedSpriteNames[i] ?? "")));
            return seq;
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
            if (AddPackableItems(m_Packables, objects, "Sprite Atlas")) m_Sprites = null;
            m_PackablesModified = true;
            EditorUtility.SetDirty(this);
        }

        internal static bool AddPackableItems(List<PackableEntry> list, Object[] objects, string targetLabel)
        {
            bool added = false;
            if (objects == null) return false;
            for (int i = 0; i < objects.Length; i++)
            {
                var obj = objects[i];
                if (obj == null)
                {
                    Debug.LogWarning($"Attempted to add null object into a {targetLabel}.");
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
                    Debug.LogWarning($"Attempted to add invalid object into a {targetLabel}. {obj.name} was a {obj.GetType().Name}. Only Folder, Texture and Sprite are valid.");
                    continue;
                }
                list.Add(new PackableEntry(ResolvePackableRef(obj), obj));
                added = true;
            }
            return added;
        }

        /// <summary>Removes all occurrences of each object in <paramref name="objects"/> by
        /// swapping each matched entry with the last entry (preserving exact object identity,
        /// including destroyed runtime objects, while ignoring null and unmatched inputs with no
        /// warnings), marking the atlas dirty once per element plus once after the loop (matches
        /// the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor v1remove/v2remove).</summary>
        internal void RemovePackables(Object[] objects)
        {
            if (RemovePackableItems(m_Packables, objects, this)) m_Sprites = null;
            m_PackablesModified = true;
            EditorUtility.SetDirty(this);
        }

        internal static bool RemovePackableItems(List<PackableEntry> list, Object[] objects, Object dirtyTarget)
        {
            bool removed = false;
            if (objects == null) return false;
            for (int i = 0; i < objects.Length; i++)
            {
                var target = objects[i];
                if (!ReferenceEquals(target, null))
                {
                    string targetGuid = null;
                    long targetFileId = 0;
                    bool hasAssetRef = target != null
                        && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out targetGuid, out targetFileId)
                        && !string.IsNullOrEmpty(targetGuid);

                    int j = 0;
                    while (j < list.Count)
                    {
                        var entry = list[j];
                        bool matches = (!ReferenceEquals(entry.Live, null) && ReferenceEquals(entry.Live, target))
                            || (hasAssetRef
                                && !string.IsNullOrEmpty(entry.Reference.Guid)
                                && entry.Reference.FileId == targetFileId
                                && string.Equals(entry.Reference.Guid, targetGuid, StringComparison.OrdinalIgnoreCase));
                        if (matches)
                        {
                            int last = list.Count - 1;
                            list[j] = list[last];
                            list.RemoveAt(last);
                            removed = true;
                        }
                        else
                        {
                            j++;
                        }
                    }
                }
                EditorUtility.SetDirty(dirtyTarget);
            }
            return removed;
        }

        internal static ObjectRef ResolvePackableRef(Object obj)
        {
            if (obj != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId) && !string.IsNullOrEmpty(guid))
                return new ObjectRef(localId, guid, AssetDatabase.IsNativeAsset(obj) ? 2 : 3);
            return default;
        }

        internal YText ToPackablesYText()
        {
            var seq = YText.Seq(flow: m_Packables.Count == 0);
            foreach (var entry in m_Packables)
                seq.Items.Add(SceneFiles.FlowValue(UnityYamlWriter.Writer.ReferenceText(entry.CurrentRef)));
            return seq;
        }

        /// <summary>The master atlas of a variant (null for an atlas without one), re-resolving
        /// its serialized reference if the master asset was reimported.</summary>
        internal SpriteAtlas MasterAtlas
        {
            get
            {
                if (!string.IsNullOrEmpty(m_MasterRef.Guid) && m_MasterRef.FileId != 0
                    && (m_Master == null || !AssetDatabaseRuntime.TryGetLocation(m_Master, out _, out _)))
                {
                    var current = AssetDatabaseRuntime.Load(m_MasterRef.Guid, m_MasterRef.FileId, typeof(SpriteAtlas)) as SpriteAtlas;
                    if (current != null) m_Master = current;
                }
                return m_Master;
            }
        }

        /// <summary>The master atlas reference as stored.</summary>
        internal ObjectRef MasterAtlasRef => m_MasterAtlasModified
            ? (m_Master != null ? ResolveMasterRef(m_Master) : default)
            : m_MasterRef;

        /// <summary>Whether m_MasterAtlas has been updated in memory.</summary>
        internal bool MasterAtlasModified => m_MasterAtlasModified;

        internal static ObjectRef ResolveMasterRef(SpriteAtlas atlas)
        {
            if (atlas != null
                && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(atlas, out string guid, out long localId)
                && !string.IsNullOrEmpty(guid))
                return new ObjectRef(atlas.m_FromImporter ? ImportedFileId : localId, guid, atlas.m_FromImporter ? 3 : 2);
            return default;
        }

        /// <summary>Sets the master atlas of a variant atlas; warns and leaves non-variant atlases
        /// unchanged and clean, returns without dirtying when unchanged, clears the master with a
        /// warning on self-assignment or a variant master (preserving tag and marking dirty), copies
        /// the valid master's tag (leaving tag unchanged when cleared), clamps an out-of-range
        /// variant scale to [0.1, 1], and marks the atlas dirty once (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/spriteatlaseditor v1masteratlas/v2masteratlas).</summary>
        internal void WriteMasterAtlas(SpriteAtlas value)
        {
            if (value == null) value = null;
            // The settings as imported, before the master changes.
            EnsureImporterSettingsRead();
            if (!m_IsVariant)
            {
                Debug.LogWarning("Cannot set a master to this atlas because it was not set as a variant atlas.");
                return;
            }
            var current = MasterAtlas;
            if (current == value) return;
            m_Master = value;
            m_MasterRef = ResolveMasterRef(value);
            if (ReferenceEquals(m_Master, this))
            {
                Debug.LogWarning("Cannot set itself as the master atlas.");
                m_Master = null;
                m_MasterRef = default;
            }
            else if (m_Master != null && m_Master.m_IsVariant)
            {
                Debug.LogWarning("Cannot set a variant atlas as a master atlas.");
                m_Master = null;
                m_MasterRef = default;
            }
            if (m_Master != null)
            {
                m_Tag = m_Master.tag;
                m_TagModified = true;
            }
            m_Settings.VariantScale = ClampVariantScale(m_Settings.VariantScale);
            m_MasterAtlasModified = true;
            m_Sprites = null;
            EditorUtility.SetDirty(this);
        }

        /// <summary>The serialized tag string.</summary>
        internal string RawTag => m_Tag;

        /// <summary>Whether m_Tag has been updated in memory.</summary>
        internal bool TagModified => m_TagModified;

        /// <summary>Whether SetIsVariant has updated m_IsVariant in memory.</summary>
        internal bool IsVariantModified => m_IsVariantModified;

        /// <summary>Sets whether this atlas is a variant; no-op when unchanged (does not dirty).
        /// Clearing variant mode clears the master atlas reference and resets the tag to this
        /// atlas's name while preserving packables, variant scale and all settings (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor v1isvariant/v2isvariant).</summary>
        internal void WriteIsVariant(bool value)
        {
            if (m_IsVariant == value) return;
            m_IsVariant = value;
            m_IsVariantModified = true;
            if (!value)
            {
                m_Master = null;
                m_MasterRef = default;
                m_MasterAtlasModified = true;
                m_Tag = name;
                m_TagModified = true;
            }
            m_Sprites = null;
            EditorUtility.SetDirty(this);
        }

        /// <summary>The variant scale multiplier (default 1 when omitted or on a new atlas).</summary>
        internal float VariantScale => Settings.VariantScale;

        /// <summary>A variant scale within [0.1, 1] (NaN stays NaN).</summary>
        static float ClampVariantScale(float value) =>
            value < MinVariantScale ? MinVariantScale : value > MaxVariantScale ? MaxVariantScale : value;

        /// <summary>Clamps <paramref name="value"/> to [0.1, 1] (leaving NaN unchanged) and
        /// unconditionally marks the atlas dirty (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/spriteatlaseditor v1variantscale/v2variantscale).</summary>
        internal void WriteVariantScale(float value)
        {
            Settings.VariantScale = ClampVariantScale(value);
            EditorUtility.SetDirty(this);
        }

        /// <summary>Whether the atlas was imported from a V2 `.spriteatlasv2` asset rather than a
        /// V1 native `.spriteatlas` file.</summary>
        internal bool IsFromImporter => m_FromImporter;

        /// <summary>The atlas's Editor settings (a V2 atlas's read from its importer on first use).</summary>
        internal SpriteAtlasEditorSettings Settings
        {
            get
            {
                EnsureImporterSettingsRead();
                return m_Settings;
            }
        }

        /// <summary>Whether builds include the atlas ("Include in Build").</summary>
        internal bool IncludeInBuild => Settings.IncludeInBuild;

        /// <summary>Sets whether builds include the atlas, marking it dirty only when the value
        /// changes (matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor).</summary>
        internal void WriteIncludeInBuild(bool value)
        {
            if (Settings.IncludeInBuild == value) return;
            m_Settings.IncludeInBuild = value;
            EditorUtility.SetDirty(this);
        }

        /// <summary>The atlas's packing settings (a V2 variant copies its non-variant master's
        /// settings on import, and later SetPackingSettings calls update the atlas itself).</summary>
        internal SpriteAtlasPackingSettings PackingSettings => Settings.Packing;

        /// <summary>Replaces the atlas's packing settings verbatim and marks it dirty (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor v1packing/v2packing).</summary>
        internal void WritePackingSettings(SpriteAtlasPackingSettings src)
        {
            Settings.Packing = src;
            EditorUtility.SetDirty(this);
        }

        internal SpriteAtlasTextureSettings TextureSettings => Settings.Texture;

        /// <summary>Replaces the atlas's texture settings verbatim and marks it dirty (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor v1texture/v2texture).</summary>
        internal void WriteTextureSettings(SpriteAtlasTextureSettings src)
        {
            Settings.Texture = src;
            EditorUtility.SetDirty(this);
        }

        /// <summary>A fresh copy of the atlas's platform settings for <paramref name="buildTarget"/>,
        /// or fresh Editor defaults named after the normalized target when no entry matches.</summary>
        internal TextureImporterPlatformSettings ReadPlatformSettings(string buildTarget) => Settings.PlatformSettings(buildTarget);

        /// <summary>Replaces a matching platform entry in place, appends a new entry when overridden
        /// or named DefaultTexturePlatform, and unconditionally marks the atlas dirty (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor v1platformset/v2platformset).</summary>
        internal void WritePlatformSettings(TextureImporterPlatformSettings src)
        {
            Settings.SetPlatformSettings(src);
            EditorUtility.SetDirty(this);
        }

        /// <summary>Reads a V2 atlas's settings from its importer's `.meta` the first time they are
        /// asked for (once the atlas's asset is known); a V2 variant takes the packing settings its
        /// master had when the variant was imported (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/spriteatlaseditor v2packing).</summary>
        void EnsureImporterSettingsRead()
        {
            if (m_ImporterSettingsRead) return;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(this, out string guid, out long _) || string.IsNullOrEmpty(guid)) return;
            m_ImporterSettingsRead = true;
            var settings = ReadImporterSettings(guid, name);
            if (settings == null) return;
            m_Settings = settings;
            if (m_MasterPackingAtImport is { } packing) m_Settings.Packing = packing;
        }

        /// <summary>The settings in a V2 atlas's importer `.meta`; null (defaults) when it has none.</summary>
        static SpriteAtlasEditorSettings ReadImporterSettings(string guid, string atlasName)
        {
            try
            {
                var docs = Native.LoadDocuments("meta:" + guid);
                return docs.Count > 0 && docs[0].TypeName == "SpriteAtlasImporter"
                    ? SpriteAtlasEditorSettings.Read(docs[0].Body, prefixed: false)
                    : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: the import settings of sprite atlas {atlasName} could not be read ({e.Message}); it reports default settings");
                return null;
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
            // A V2 atlas's settings are its importer's, read when first asked for.
            m_Settings = m_FromImporter ? new SpriteAtlasEditorSettings() : SpriteAtlasEditorSettings.Read(data, prefixed: true);
            m_ImporterSettingsRead = !m_FromImporter;
            m_IsVariant = body.Bool("m_IsVariant");
            m_IsVariantModified = false;
            m_Tag = body.Str("m_Tag", null);
            m_TagModified = false;
            var master = body.Ref("m_MasterAtlas");
            m_MasterRef = master;
            m_Master = master.FileId != 0 ? resolver.Resolve(master, typeof(SpriteAtlas)) as SpriteAtlas : null;
            m_MasterAtlasModified = false;
            if (string.IsNullOrEmpty(m_Tag) && m_IsVariant && m_Master != null)
                m_Tag = m_Master.tag;
            // A V2 variant imports its master's packing settings as they are now: the master's
            // live settings once read, else its importer's.
            m_MasterPackingAtImport = null;
            if (m_FromImporter && m_IsVariant && m_Master != null && !ReferenceEquals(m_Master, this) && !m_Master.m_IsVariant)
                m_MasterPackingAtImport = m_Master.m_ImporterSettingsRead
                    ? m_Master.m_Settings.Packing
                    : ReadImporterSettings(master.Guid, m_Master.name)?.Packing ?? SpriteAtlasPackingSettings.Default;
            m_SerializedPackedSprites.Clear();
            foreach (var packedSprite in body.Get("m_PackedSprites")?.Seq() ?? Array.Empty<YNode>())
                m_SerializedPackedSprites.Add(packedSprite.AsRef());
            m_SerializedPackedSpriteNames.Clear();
            foreach (var packedName in body.Get("m_PackedSpriteNamesToIndex")?.Seq() ?? Array.Empty<YNode>())
                m_SerializedPackedSpriteNames.Add(packedName.Scalar ?? "");
            m_PackedDataModified = false;
            m_Sprites = null;
            EditorUtility.ClearDirty(this);
        }
    }
}
