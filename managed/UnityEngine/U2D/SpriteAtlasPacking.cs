using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;
using Object = UnityEngine.Object;

namespace UnityEngine.U2D
{
    /// <summary>
    /// The sprite atlases the project's Sprite Packer mode packs for Play mode, as the Unity
    /// 6000.0.71f1 Editor packs them before it enters Play mode (tests/projects/spriteatlas): in Play
    /// mode an imported sprite of a packed atlas draws and reports from the atlas's page texture.
    /// The sprites of an atlas that is not included in builds bind late: they have no texture until
    /// an <see cref="SpriteAtlasManager.atlasRequested"/> listener registers the atlas. Outside
    /// Play mode sprites stay unpacked, as in the Editor.
    /// </summary>
    internal static class SpriteAtlasPacking
    {
        /// <summary>A page texture; its sprites' rects and UVs refer to the UV size (a variant's
        /// master's page size).</summary>
        sealed class Page { public string Key, Name; public int Width, Height, UvWidth, UvHeight; public Texture2D Texture; }

        sealed class Atlas
        {
            public string Guid, Name, Tag, Master;
            public bool IncludeInBuild, Tight, Registered;
            public float Scale = 1f;
            public Page[] Pages;
            public List<Binding> Sprites = new List<Binding>();
            public Dictionary<(string, long), Binding> ByKey = new Dictionary<(string, long), Binding>();
            /// <summary>The loaded sprites in GetSprites order, once asked for.</summary>
            public List<Sprite> Loaded;
        }

        sealed class Binding
        {
            public Atlas Atlas;
            public string Texture;
            public long FileId;
            public int Page, Rotation;
            public Rect Rect;
            public Vector4 UvTransform;
        }

        static Dictionary<(string, long), List<Binding>> s_BuiltInBySprite;
        static Dictionary<(string, long), List<Binding>> s_ExcludedBySprite;
        static Dictionary<string, Atlas> s_Atlases;
        static readonly Dictionary<string, List<Atlas>> s_RegisteredByTag = new Dictionary<string, List<Atlas>>(StringComparer.Ordinal);
        /// <summary>The build target each atlas in <see cref="s_Atlases"/> was last packed for.</summary>
        static readonly Dictionary<string, UnityEditor.BuildTarget> s_PackedTargets = new Dictionary<string, UnityEditor.BuildTarget>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Atlases already packed by Play-mode startup packing.</summary>
        static readonly HashSet<string> s_PlayModePacked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>A sprite larger than its atlas's page cancelled the packing: every atlas is
        /// empty (measured: tests/projects/spriteatlas set `over`).</summary>
        static bool s_Cancelled;
        /// <summary>Late-binding sprites waiting for an atlas with one tag, each once, in the order
        /// they started waiting.</summary>
        sealed class Waiting
        {
            internal readonly List<Sprite> Sprites = new List<Sprite>();
            readonly HashSet<Sprite> m_Added = new HashSet<Sprite>(ReferenceEqualityComparer.Instance);

            internal void Add(Sprite sprite)
            {
                if (m_Added.Add(sprite)) Sprites.Add(sprite);
            }
        }

        /// <summary>Late-binding sprites waiting for their atlas, by atlas tag.</summary>
        static readonly Dictionary<string, Waiting> s_Waiting = new Dictionary<string, Waiting>(StringComparer.Ordinal);
        static readonly List<string> s_Requests = new List<string>();
        static readonly List<Sprite> s_None = new List<Sprite>();

        sealed class Json
        {
            public string Cancelled { get; set; }
            public List<AtlasJson> Atlases { get; set; }
        }

        sealed class AtlasJson
        {
            public string Guid { get; set; }
            public string Name { get; set; }
            public string Tag { get; set; }
            public bool IncludeInBuild { get; set; }
            public bool Tight { get; set; }
            public string Master { get; set; }
            public float Scale { get; set; }
            public List<PageJson> Pages { get; set; }
            public List<SpriteJson> Sprites { get; set; }
        }

        sealed class PageJson
        {
            public string Key { get; set; }
            public string Name { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int UvWidth { get; set; }
            public int UvHeight { get; set; }
        }

        sealed class SpriteJson
        {
            public string Texture { get; set; }
            public long FileId { get; set; }
            public int Page { get; set; }
            public float[] Rect { get; set; }
            public float[] UvTransform { get; set; }
            public int Rotation { get; set; }
        }

        static readonly JsonSerializerOptions s_Options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        /// <summary>Whether sprites bind to packed atlases now: in Play mode, with the native side up.</summary>
        static bool Active => Application.isPlaying && Native.IsReady;

        static void Load()
        {
            if (s_Atlases != null) return;
            s_BuiltInBySprite = new Dictionary<(string, long), List<Binding>>();
            s_ExcludedBySprite = new Dictionary<(string, long), List<Binding>>();
            s_Atlases = new Dictionary<string, Atlas>(StringComparer.OrdinalIgnoreCase);
            var json = JsonSerializer.Deserialize<Json>(Native.Call("sprite-atlases", "{}"), s_Options);
            s_Cancelled = json?.Cancelled != null;
            if (json?.Atlases == null) return;
            var activeTarget = UnityEditor.EditorUserBuildSettings.activeBuildTarget;
            foreach (var a in json.Atlases)
            {
                var atlas = BuildAtlas(a);
                s_Atlases[a.Guid] = atlas;
                s_PackedTargets[a.Guid] = activeTarget;
                s_PlayModePacked.Add(a.Guid);
            }
            RebuildSpriteTables();
        }

        /// <summary>Rebuilds the per-sprite built-in and excluded atlas lookup tables from
        /// <see cref="s_Atlases"/>. Built-in (<c>IncludeInBuild == true</c>) candidates are sorted
        /// as the Unity 6000.0.71f1 Editor sorts them (tests/projects/spriteatlasmultibind): masters
        /// before variants, then ascending by Unity GUID's four nibble-reversed 32-bit words.</summary>
        static void RebuildSpriteTables()
        {
            s_BuiltInBySprite ??= new Dictionary<(string, long), List<Binding>>();
            s_ExcludedBySprite ??= new Dictionary<(string, long), List<Binding>>();
            s_BuiltInBySprite.Clear();
            s_ExcludedBySprite.Clear();
            if (s_Atlases == null) return;
            // A variant atlas shares its master's tag (V2 .spriteatlasv2 omits m_Tag, so resolve
            // it from the master atlas here as SpriteAtlas.ReadSerialized does).
            foreach (var atlas in s_Atlases.Values)
                if (atlas.Master != null && s_Atlases.TryGetValue(atlas.Master, out var master) && !string.IsNullOrEmpty(master.Tag))
                    atlas.Tag = master.Tag;
            foreach (var atlas in s_Atlases.Values)
            {
                var target = atlas.IncludeInBuild ? s_BuiltInBySprite : s_ExcludedBySprite;
                for (int i = 0; i < atlas.Sprites.Count; i++)
                {
                    var binding = atlas.Sprites[i];
                    var key = (binding.Texture.ToLowerInvariant(), binding.FileId);
                    if (!target.TryGetValue(key, out var list))
                        target[key] = list = new List<Binding>(1);
                    list.Add(binding);
                }
            }
            foreach (var list in s_BuiltInBySprite.Values)
                if (list.Count > 1) list.Sort(CompareBuiltInBinding);
        }

        static int CompareBuiltInBinding(Binding a, Binding b) =>
            CompareBuiltInAtlas(a.Atlas.Master != null, a.Atlas.Guid, b.Atlas.Master != null, b.Atlas.Guid);

        /// <summary>Built-in atlas selection order when multiple <c>Include in Build</c> atlases
        /// contain the same sprite: master (<c>false</c>) before variant (<c>true</c>), then
        /// ascending by Unity GUID's four 32-bit words (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/spriteatlasmultibind).</summary>
        internal static int CompareBuiltInAtlas(bool aIsVariant, string aGuid, bool bIsVariant, string bGuid)
        {
            int byVariant = (aIsVariant ? 1 : 0).CompareTo(bIsVariant ? 1 : 0);
            if (byVariant != 0) return byVariant;
            return CompareUnityGuid(aGuid, bGuid);
        }

        /// <summary>Compares two 32-hex-digit Unity asset GUID strings in Unity's four unsigned
        /// 32-bit word order, where each 8-hex-digit chunk stores nibbles from least significant
        /// (<c>index + 0</c>) to most significant (<c>index + 7</c>).</summary>
        internal static int CompareUnityGuid(string a, string b)
        {
            if (TryParseUnityGuidWords(a, out uint a0, out uint a1, out uint a2, out uint a3)
                && TryParseUnityGuidWords(b, out uint b0, out uint b1, out uint b2, out uint b3))
            {
                int c = a0.CompareTo(b0); if (c != 0) return c;
                c = a1.CompareTo(b1); if (c != 0) return c;
                c = a2.CompareTo(b2); if (c != 0) return c;
                return a3.CompareTo(b3);
            }
            return StringComparer.OrdinalIgnoreCase.Compare(a, b);
        }

        static bool TryParseUnityGuidWords(string hex, out uint w0, out uint w1, out uint w2, out uint w3)
        {
            w0 = w1 = w2 = w3 = 0;
            if (string.IsNullOrEmpty(hex) || hex.Length != 32) return false;
            return TryParseWord(hex, 0, out w0)
                && TryParseWord(hex, 8, out w1)
                && TryParseWord(hex, 16, out w2)
                && TryParseWord(hex, 24, out w3);
        }

        static bool TryParseWord(string hex, int offset, out uint word)
        {
            word = 0;
            for (int nibble = 0; nibble < 8; nibble++)
            {
                int digit = HexDigit(hex[offset + nibble]);
                if (digit < 0) return false;
                word |= (uint)digit << (nibble * 4);
            }
            return true;
        }

        static int HexDigit(char c) =>
            c >= '0' && c <= '9' ? c - '0' :
            c >= 'a' && c <= 'f' ? c - 'a' + 10 :
            c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

        static Atlas BuildAtlas(AtlasJson a)
        {
            var atlas = new Atlas
            {
                Guid = a.Guid, Name = a.Name, Tag = a.Tag, Master = a.Master,
                IncludeInBuild = a.IncludeInBuild, Tight = a.Tight, Scale = a.Scale,
                Pages = a.Pages.ConvertAll(p => new Page { Key = p.Key, Name = p.Name, Width = p.Width, Height = p.Height, UvWidth = p.UvWidth, UvHeight = p.UvHeight }).ToArray(),
            };
            foreach (var s in a.Sprites)
            {
                var binding = new Binding
                {
                    Atlas = atlas, Texture = s.Texture, FileId = s.FileId, Page = s.Page, Rotation = s.Rotation,
                    Rect = new Rect(s.Rect[0], s.Rect[1], s.Rect[2], s.Rect[3]),
                    UvTransform = new Vector4(s.UvTransform[0], s.UvTransform[1], s.UvTransform[2], s.UvTransform[3]),
                };
                atlas.Sprites.Add(binding);
                atlas.ByKey[(s.Texture.ToLowerInvariant(), s.FileId)] = binding;
            }
            return atlas;
        }

        static Texture2D PageTexture(Page page)
        {
            if (page.Texture != null) return page.Texture;
            ulong handle = RenderHandles.Next();
            if (!Native.ImportTexture(handle, page.Key, out var imported))
                throw new InvalidOperationException($"the sprite atlas page {page.Name} could not be imported");
            // The page key stands for its GUID: readable pages read their texels by it.
            page.Texture = Texture2D.FromImport(handle, page.Key, page.Name, imported);
            return page.Texture;
        }

        static void Bind(Sprite sprite, Binding binding, bool reportAsPacked = true)
        {
            var page = binding.Atlas.Pages[binding.Page];
            sprite.BindAtlas(PageTexture(page), binding.Rect, binding.UvTransform,
                binding.Atlas.Tight ? SpritePackingMode.Tight : SpritePackingMode.Rectangle, (SpritePackingRotation)binding.Rotation,
                new Vector2(page.UvWidth, page.UvHeight), binding.Atlas.Scale, reportAsPacked);
        }

        /// <summary>Binds an imported sprite (its texture's GUID and its file id) to its packed atlas
        /// in Play mode (matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlas and
        /// tests/projects/spriteatlasmultibind).</summary>
        internal static void Apply(Sprite sprite, string textureGuid, long fileId)
        {
            if (!Active) return;
            Load();
            var key = (textureGuid.ToLowerInvariant(), fileId);
            if (s_BuiltInBySprite.TryGetValue(key, out var builtIn) && builtIn.Count > 0)
            {
                sprite.m_AtlasBinding = key;
                if (builtIn.Count > 1)
                    Debug.LogWarning($"Sprite {sprite.name} matches more than one built-in atlases. Default to use the first available atlas.");
                Bind(sprite, builtIn[0]);
                return;
            }
            if (!s_ExcludedBySprite.TryGetValue(key, out var excluded) || excluded.Count == 0) return;
            sprite.m_AtlasBinding = key;

            var tags = new List<string>(excluded.Count);
            for (int i = 0; i < excluded.Count; i++)
            {
                var tag = excluded[i].Atlas.Tag;
                if (!string.IsNullOrEmpty(tag) && !tags.Contains(tag)) tags.Add(tag);
            }
            tags.Sort(StringComparer.Ordinal);

            // If one or more matching atlases were already registered via SpriteAtlasManager, bind now.
            List<Binding> registeredMatches = null;
            for (int i = 0; i < tags.Count; i++)
            {
                if (!s_RegisteredByTag.TryGetValue(tags[i], out var registered)) continue;
                for (int j = 0; j < registered.Count; j++)
                {
                    if (registered[j].ByKey.TryGetValue(key, out var regBinding))
                    {
                        registeredMatches ??= new List<Binding>(1);
                        if (!registeredMatches.Contains(regBinding)) registeredMatches.Add(regBinding);
                    }
                }
            }
            if (registeredMatches != null && registeredMatches.Count > 0)
            {
                if (registeredMatches.Count > 1)
                {
                    var names = new List<string>(registeredMatches.Count);
                    for (int i = 0; i < registeredMatches.Count; i++) names.Add(registeredMatches[i].Atlas.Name);
                    Debug.LogWarning($"Sprite {sprite.name} matches more than one atlases ({string.Join(", ", names)}). Default to first atlas.");
                }
                Bind(sprite, registeredMatches[0]);
                return;
            }

            // Late binding: no texture until an atlas for one of its tags is registered.
            sprite.UnbindTexture();
            for (int i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                if (!s_Waiting.TryGetValue(tag, out var waiting))
                    s_Waiting[tag] = waiting = new Waiting();
                waiting.Add(sprite);
                if (!s_Requests.Contains(tag)) s_Requests.Add(tag);
            }
        }

        /// <summary>Asks the listeners for the atlases late-binding sprites wait for, in ordinal tag
        /// order (PlayerLoop EarlyUpdate.SpriteAtlasManagerUpdate).</summary>
        internal static void DispatchRequests()
        {
            if (s_Requests.Count == 0) return;
            s_Requests.Sort(StringComparer.Ordinal);
            var tags = s_Requests.ToArray();
            s_Requests.Clear();
            foreach (var tag in tags)
                if (!SpriteAtlasManager.RequestAtlas(tag))
                    Debug.LogWarning($"SpriteAtlasManager.atlasRequested wasn't listened to while {tag} requested.");
        }

        /// <summary>An atlas given to an atlasRequested callback: its waiting sprites bind to it
        /// (whether master or variant; a sprite waiting on multiple excluded tags re-binds to each
        /// matching atlas registered, matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/spriteatlasmultibind).</summary>
        internal static void Register(SpriteAtlas spriteAtlas)
        {
            if (spriteAtlas == null || !Active) return;
            Load();
            var atlas = Find(spriteAtlas);
            if (atlas == null) return;
            atlas.Registered = true;
            if (!s_RegisteredByTag.TryGetValue(atlas.Tag, out var registered))
                s_RegisteredByTag[atlas.Tag] = registered = new List<Atlas>(1);
            if (!registered.Contains(atlas)) registered.Add(atlas);
            if (!s_Waiting.TryGetValue(atlas.Tag, out var waiting)) return;
            s_Waiting.Remove(atlas.Tag);
            foreach (var sprite in waiting.Sprites)
                if (sprite != null && sprite.m_AtlasBinding is (string Texture, long FileId) key
                    && atlas.ByKey.TryGetValue(key, out var binding))
                    Bind(sprite, binding);
        }

        static Atlas Find(SpriteAtlas spriteAtlas)
        {
            if (s_Atlases == null || !AssetDatabaseRuntime.TryGetLocation(spriteAtlas, out var guid, out _)) return null;
            return s_Atlases.TryGetValue(guid, out var atlas) ? atlas : null;
        }

        /// <summary>A packed atlas's sprites in GetSprites order (a variant's are its master's), or
        /// null when the atlas is not packed now.</summary>
        internal static List<Sprite> PackedSprites(SpriteAtlas spriteAtlas)
        {
            if (Active) Load();
            if (s_Atlases == null) return null;
            if (s_Cancelled) return s_None;
            var atlas = Find(spriteAtlas);
            if (atlas == null) return null;
            if (atlas.Loaded != null) return atlas.Loaded;
            var sprites = new List<Sprite>(atlas.Sprites.Count);
            foreach (var b in atlas.Sprites)
                if (AssetDatabaseRuntime.Load(b.Texture, b.FileId, typeof(Sprite)) is Sprite s) sprites.Add(s);
            return atlas.Loaded = sprites;
        }

        /// <summary>A copy of a packed atlas's sprite as SpriteAtlas.GetSprite gives it: on that atlas's
        /// page (a variant's scaled page), also before a late-binding atlas is registered.</summary>
        internal static Sprite Clone(SpriteAtlas spriteAtlas, Sprite sprite)
        {
            var copy = (Sprite)Object.Instantiate(sprite);
            if (Active) Load();
            if (s_Atlases == null) return copy;
            (string Texture, long FileId) key;
            if (sprite.m_AtlasBinding is (string boundTex, long boundId))
                key = (boundTex, boundId);
            else if (UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out var guid, out var fileId) && !string.IsNullOrEmpty(guid))
                key = (guid.ToLowerInvariant(), fileId);
            else
                return copy;
            var atlas = Find(spriteAtlas);
            // On the pages of the atlas it comes from, whichever atlas the sprite itself binds to
            // (measured: a master left out of builds gives clones on its own pages; outside Play-mode
            // packing modes the clone binds its texture to the packed page while `packed` stays false
            // and `spriteAtlasTextureScale` stays 1, tests/projects/spriteatlaspack).
            if (atlas != null && atlas.ByKey.TryGetValue(key, out var binding))
                Bind(copy, binding, reportAsPacked: UnityEditor.EditorSettings.IsPlayModeAtlasPackingActive);
            return copy;
        }

        /// <summary>Packs persistent V1 <see cref="SpriteAtlas"/> assets through the native sprite
        /// atlas packer, refreshes in-memory packed atlas state, and persists updated V1 packed
        /// sprite lists to disk (matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaspack).</summary>
        internal static void PackV1(IReadOnlyList<SpriteAtlas> requestedOrder, UnityEditor.BuildTarget target)
        {
            if (requestedOrder == null || requestedOrder.Count == 0 || !Native.IsReady) return;
            if (s_Atlases == null) Load();

            var entriesByGuid = new Dictionary<string, (SpriteAtlas Atlas, string Guid, string Path, bool WasDirty, bool HadPlayModePack, List<(string, long)> PrevKeys)>(StringComparer.OrdinalIgnoreCase);
            var uniquePaths = new List<string>();
            for (int i = 0; i < requestedOrder.Count; i++)
            {
                var sa = requestedOrder[i];
                if (sa == null || !AssetDatabaseRuntime.TryGetLocation(sa, out var guid, out _) || string.IsNullOrEmpty(guid))
                    continue;
                if (entriesByGuid.ContainsKey(guid)) continue;
                var path = AssetDatabaseRuntime.PathOf(guid);
                if (string.IsNullOrEmpty(path)) continue;
                bool wasDirty = UnityEditor.EditorUtility.IsDirty(sa);
                bool hadPlayModePack = s_PlayModePacked.Contains(guid);
                List<(string, long)> prevKeys = null;
                if (s_Atlases.TryGetValue(guid, out var existing))
                {
                    prevKeys = new List<(string, long)>(existing.Sprites.Count);
                    for (int k = 0; k < existing.Sprites.Count; k++)
                        prevKeys.Add((existing.Sprites[k].Texture.ToLowerInvariant(), existing.Sprites[k].FileId));
                }
                entriesByGuid[guid] = (sa, guid, path, wasDirty, hadPlayModePack, prevKeys);
                uniquePaths.Add(path);
            }
            if (uniquePaths.Count == 0) return;

            // In Play-mode packing (AlwaysOnAtlas), packing atlases whose cached pack already matches
            // the target reconciles their packed sprites (masters first, then variants, without
            // deduplicating the input array), resetting and re-resolving sprites against built-in
            // atlases and warning when a sprite matches more than one built-in atlas.
            if (UnityEditor.EditorSettings.IsPlayModeAtlasPackingActive)
            {
                var resetSprites = new HashSet<(string, long)>();
                for (int pass = 0; pass < 2; pass++)
                {
                    bool wantVariant = pass == 1;
                    for (int i = 0; i < requestedOrder.Count; i++)
                    {
                        var sa = requestedOrder[i];
                        if (sa == null || sa.isVariant != wantVariant) continue;
                        if (!AssetDatabaseRuntime.TryGetLocation(sa, out var guid, out _) || !entriesByGuid.TryGetValue(guid, out var entry))
                            continue;
                        if (entry.WasDirty) continue;
                        if (!s_PackedTargets.TryGetValue(guid, out var prevTarget) || prevTarget != target) continue;
                        if (!s_Atlases.TryGetValue(guid, out var existing)) continue;
                        for (int k = 0; k < existing.Sprites.Count; k++)
                        {
                            var b = existing.Sprites[k];
                            var key = (b.Texture.ToLowerInvariant(), b.FileId);
                            if (resetSprites.Contains(key) && CountBuiltInAtlasesContaining(key) > 1)
                            {
                                var sprite = AssetDatabaseRuntime.Load(b.Texture, b.FileId, typeof(Sprite)) as Sprite;
                                Debug.LogWarning($"Sprite {(sprite != null ? sprite.name : "")} matches more than one built-in atlases. Default to use the first available atlas.");
                            }
                            resetSprites.Add(key);
                        }
                    }
                }
            }

            // Flush any pending in-memory edits on the V1 atlases to disk so the native packer reads
            // their updated packables and settings.
            foreach (var entry in entriesByGuid.Values)
                if (entry.WasDirty)
                    AssetFiles.SaveDirty(entry.Atlas);

            var payload = JsonSerializer.Serialize(new { paths = uniquePaths }, s_Options);
            var json = JsonSerializer.Deserialize<Json>(Native.Call("sprite-atlases-pack", payload), s_Options);
            if (json?.Cancelled != null || json?.Atlases == null) return;

            foreach (var a in json.Atlases)
            {
                var atlas = BuildAtlas(a);
                s_Atlases[a.Guid] = atlas;
                s_PackedTargets[a.Guid] = target;
                if (!entriesByGuid.TryGetValue(a.Guid, out var entry)) continue;

                var newKeys = new List<(string, long)>(atlas.Sprites.Count);
                var packedRefs = new List<ObjectRef>(atlas.Sprites.Count);
                var packedNames = new List<string>(atlas.Sprites.Count);
                for (int k = 0; k < atlas.Sprites.Count; k++)
                {
                    var b = atlas.Sprites[k];
                    var key = (b.Texture.ToLowerInvariant(), b.FileId);
                    newKeys.Add(key);
                    packedRefs.Add(new ObjectRef(b.FileId, key.Item1, 3));
                    var sprite = AssetDatabaseRuntime.Load(b.Texture, b.FileId, typeof(Sprite)) as Sprite;
                    packedNames.Add(sprite != null ? sprite.name : "");
                }

                bool serializedMatches = KeysEqual(entry.Atlas.SerializedPackedSprites, newKeys);
                bool needWrite = entry.WasDirty
                    || (!entry.HadPlayModePack && !serializedMatches)
                    || (entry.HadPlayModePack && !KeysEqual(entry.PrevKeys, newKeys));

                entry.Atlas.ApplyPackedSprites(packedRefs, packedNames, markModified: needWrite);
                if (needWrite)
                {
                    UnityEditor.EditorUtility.SetDirty(entry.Atlas);
                    AssetFiles.SaveDirty(entry.Atlas);
                }
                else
                {
                    UnityEditor.EditorUtility.ClearDirty(entry.Atlas);
                }
                entry.Atlas.InvalidateCachedSprites();
            }
            RebuildSpriteTables();
        }

        static int CountBuiltInAtlasesContaining((string, long) key)
        {
            int count = 0;
            foreach (var atlas in s_Atlases.Values)
                if (atlas.IncludeInBuild && atlas.ByKey.ContainsKey(key))
                    count++;
            return count;
        }

        static bool KeysEqual(List<(string, long)> a, List<(string, long)> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].Item2 != b[i].Item2 || !string.Equals(a[i].Item1, b[i].Item1, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        static bool KeysEqual(IReadOnlyList<ObjectRef> a, List<(string, long)> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].FileId != b[i].Item2 || !string.Equals(a[i].Guid, b[i].Item1, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }
    }
}
