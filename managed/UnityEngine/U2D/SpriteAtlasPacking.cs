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
            foreach (var a in json.Atlases)
            {
                var atlas = new Atlas
                {
                    Guid = a.Guid, Name = a.Name, Tag = a.Tag, Master = a.Master,
                    IncludeInBuild = a.IncludeInBuild, Tight = a.Tight, Scale = a.Scale,
                    Pages = a.Pages.ConvertAll(p => new Page { Key = p.Key, Name = p.Name, Width = p.Width, Height = p.Height, UvWidth = p.UvWidth, UvHeight = p.UvHeight }).ToArray(),
                };
                s_Atlases[a.Guid] = atlas;
                foreach (var s in a.Sprites)
                {
                    var binding = new Binding
                    {
                        Atlas = atlas, Texture = s.Texture, FileId = s.FileId, Page = s.Page, Rotation = s.Rotation,
                        Rect = new Rect(s.Rect[0], s.Rect[1], s.Rect[2], s.Rect[3]),
                        UvTransform = new Vector4(s.UvTransform[0], s.UvTransform[1], s.UvTransform[2], s.UvTransform[3]),
                    };
                    atlas.Sprites.Add(binding);
                    var key = (s.Texture.ToLowerInvariant(), s.FileId);
                    atlas.ByKey[key] = binding;
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
            foreach (var atlas in s_Atlases.Values)
                if (atlas.Master != null && atlas.IncludeInBuild && s_Atlases.TryGetValue(atlas.Master, out var master) && !master.IncludeInBuild)
                    foreach (var pair in atlas.ByKey)
                        s_Bindings[pair.Key] = pair.Value;
            foreach (var ((first, second), n) in shared)
                Debug.LogWarning($"Unbound: {n} sprites are in sprite atlases {first} and {second}; they bind to {second} (the Editor's choice among several atlases is not reproduced)");
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

        static void Bind(Sprite sprite, Binding binding)
        {
            var page = binding.Atlas.Pages[binding.Page];
            sprite.BindAtlas(PageTexture(page), binding.Rect, binding.UvTransform,
                binding.Atlas.Tight ? SpritePackingMode.Tight : SpritePackingMode.Rectangle, (SpritePackingRotation)binding.Rotation,
                new Vector2(page.UvWidth, page.UvHeight), binding.Atlas.Scale);
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
            foreach (var sprite in waiting)
                if (sprite != null && sprite.m_AtlasBinding is (string Texture, long FileId) key
                    && (atlas.ByKey.TryGetValue(key, out var binding) || s_Bindings.TryGetValue(key, out binding)))
                    Bind(sprite, binding);
        }

        static Atlas Find(SpriteAtlas spriteAtlas)
        {
            if (!AssetDatabaseRuntime.TryGetLocation(spriteAtlas, out var guid, out _)) return null;
            return s_Atlases.TryGetValue(guid, out var atlas) ? atlas : null;
        }

        /// <summary>A packed atlas's sprites in GetSprites order (a variant's are its master's), or
        /// null when the atlas is not packed now.</summary>
        internal static List<Sprite> PackedSprites(SpriteAtlas spriteAtlas)
        {
            if (!Active) return null;
            Load();
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
            if (!Active || sprite.m_AtlasBinding is not (string Texture, long FileId) key) return copy;
            Load();
            var atlas = Find(spriteAtlas);
            // On the pages of the atlas it comes from, whichever atlas the sprite itself binds to
            // (measured: a master left out of builds gives clones on its own pages).
            if (atlas != null && atlas.ByKey.TryGetValue(key, out var binding))
                Bind(copy, binding);
            return copy;
        }
    }
}
