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

        static Dictionary<(string, long), Binding> s_Bindings;
        static Dictionary<string, Atlas> s_Atlases;
        /// <summary>The build target each atlas in <see cref="s_Atlases"/> was last packed for.</summary>
        static readonly Dictionary<string, UnityEditor.BuildTarget> s_PackedTargets = new Dictionary<string, UnityEditor.BuildTarget>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Atlases already packed by Play-mode startup packing.</summary>
        static readonly HashSet<string> s_PlayModePacked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>A sprite larger than its atlas's page cancelled the packing: every atlas is
        /// empty (measured: tests/projects/spriteatlas set `over`).</summary>
        static bool s_Cancelled;
        /// <summary>Late-binding sprites waiting for their atlas, by atlas tag.</summary>
        static readonly Dictionary<string, List<Sprite>> s_Waiting = new Dictionary<string, List<Sprite>>();
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
            if (s_Bindings != null) return;
            s_Bindings = new Dictionary<(string, long), Binding>();
            s_Atlases = new Dictionary<string, Atlas>(StringComparer.OrdinalIgnoreCase);
            var json = JsonSerializer.Deserialize<Json>(Native.Call("sprite-atlases", "{}"), s_Options);
            s_Cancelled = json?.Cancelled != null;
            if (json?.Atlases == null) return;
            var shared = new Dictionary<(string, string), int>();
            var activeTarget = UnityEditor.EditorUserBuildSettings.activeBuildTarget;
            foreach (var a in json.Atlases)
            {
                var atlas = BuildAtlas(a);
                s_Atlases[a.Guid] = atlas;
                s_PackedTargets[a.Guid] = activeTarget;
                s_PlayModePacked.Add(a.Guid);
                foreach (var binding in atlas.Sprites)
                {
                    var key = (binding.Texture.ToLowerInvariant(), binding.FileId);
                    // A variant's sprites are its master's: they bind to the master.
                    if (a.Master != null) continue;
                    // A sprite in several atlases binds to the last by path (the Editor's choice
                    // among them is unmeasured).
                    if (s_Bindings.TryGetValue(key, out var other))
                    {
                        shared.TryGetValue((other.Atlas.Name, atlas.Name), out int n);
                        shared[(other.Atlas.Name, atlas.Name)] = n + 1;
                    }
                    s_Bindings[key] = binding;
                }
            }
            // A master left out of builds with a variant in them: its sprites bind to the variant
            // (measured: tests/projects/spriteatlas, the Manual's way to use a variant).
            var variantOf = new Dictionary<string, string>();
            foreach (var atlas in s_Atlases.Values)
                if (atlas.Master != null && atlas.IncludeInBuild && s_Atlases.TryGetValue(atlas.Master, out var master) && !master.IncludeInBuild)
                {
                    if (variantOf.TryGetValue(master.Guid, out var other))
                        Debug.LogWarning($"Unbound: sprite atlas {master.Name} has several variants in builds ({other}, {atlas.Name}); its sprites bind to {atlas.Name} (the Editor's choice is not reproduced)");
                    variantOf[master.Guid] = atlas.Name;
                    foreach (var pair in atlas.ByKey)
                        s_Bindings[pair.Key] = pair.Value;
                }
            foreach (var ((first, second), n) in shared)
                Debug.LogWarning($"Unbound: {n} sprites are in sprite atlases {first} and {second}; they bind to {second} (the Editor's choice among several atlases is not reproduced)");
        }

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
        /// in Play mode.</summary>
        internal static void Apply(Sprite sprite, string textureGuid, long fileId)
        {
            if (!Active) return;
            Load();
            var key = (textureGuid.ToLowerInvariant(), fileId);
            if (!s_Bindings.TryGetValue(key, out var binding)) return;
            sprite.m_AtlasBinding = key;
            if (binding.Atlas.IncludeInBuild || binding.Atlas.Registered)
            {
                Bind(sprite, binding);
                return;
            }
            // Late binding: no texture until the atlas is registered.
            sprite.UnbindTexture();
            if (!s_Waiting.TryGetValue(binding.Atlas.Tag, out var waiting))
                s_Waiting[binding.Atlas.Tag] = waiting = new List<Sprite>();
            waiting.Add(sprite);
            if (!s_Requests.Contains(binding.Atlas.Tag)) s_Requests.Add(binding.Atlas.Tag);
        }

        /// <summary>Asks the listeners for the atlases late-binding sprites wait for
        /// (PlayerLoop EarlyUpdate.SpriteAtlasManagerUpdate).</summary>
        internal static void DispatchRequests()
        {
            if (s_Requests.Count == 0) return;
            var tags = s_Requests.ToArray();
            s_Requests.Clear();
            foreach (var tag in tags)
                if (!SpriteAtlasManager.RequestAtlas(tag))
                    Debug.LogWarning($"SpriteAtlasManager.atlasRequested wasn't listened to while {tag} requested.");
        }

        /// <summary>An atlas given to an atlasRequested callback: its waiting sprites bind.</summary>
        internal static void Register(SpriteAtlas spriteAtlas)
        {
            if (spriteAtlas == null || !Active) return;
            Load();
            var atlas = Find(spriteAtlas);
            if (atlas == null) return;
            atlas.Registered = true;
            if (!s_Waiting.TryGetValue(atlas.Tag, out var waiting)) return;
            s_Waiting.Remove(atlas.Tag);
            // A registered variant binds the sprites to its own pages (unmeasured).
            if (atlas.Master != null)
                Debug.LogWarning($"Unbound: variant atlas {atlas.Name} registered for late-binding sprites binds them to its pages (unmeasured)");
            foreach (var sprite in waiting)
                if (sprite != null && sprite.m_AtlasBinding is (string Texture, long FileId) key
                    && (atlas.ByKey.TryGetValue(key, out var binding) || s_Bindings.TryGetValue(key, out binding)))
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
            if (s_Bindings == null) Load();

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

            bool playModeActive = UnityEditor.EditorSettings.IsPlayModeAtlasPackingActive;
            foreach (var a in json.Atlases)
            {
                var atlas = BuildAtlas(a);
                s_Atlases[a.Guid] = atlas;
                s_PackedTargets[a.Guid] = target;
                if (playModeActive && a.Master == null)
                {
                    foreach (var binding in atlas.Sprites)
                        s_Bindings[(binding.Texture.ToLowerInvariant(), binding.FileId)] = binding;
                }
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
