using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;
using UnityEngine.U2D;
using UnityEngine.UI;

/// <summary>
/// The colors renderers put in their vertices, read back exactly: every case draws a quad of
/// each color in <see cref="Tints"/>, on a grid of three by three, into a float target of its own
/// camera, and the pixel at each quad's center is recorded. The built-in pipeline first, then URP's
/// Universal renderer and its 2D renderer (the pipeline set at runtime), then the Universal
/// renderer again without the SRP Batcher. Writes values.jsonl into
/// VERTEX_COLORS_OUT and exits.
/// </summary>
public sealed class VertexColorProbe : MonoBehaviour
{
    const int Size = 48;
    const float Cell = 2f / 3f, Quad = 0.5f, Half = Quad / 2f;
    const int Settle = 4;
    const int UniversalRenderer = 0, Renderer2D = 2;

    /// <summary>Channels on both sides of the sRGB curve's knee, dark ones where an 8-bit
    /// vertex color of the linear value loses most, channels whose linear byte differs by where
    /// the color is rounded to 8 bits (0.657, 0.669, 0.681, 0.7, 0.9), channels outside 0..1,
    /// alphas below one and zero.</summary>
    static readonly Color[] Tints =
    {
        new Color(0.6f, 0.6f, 0.6f, 1f),
        new Color(0.1f, 0.5f, 0.9f, 0.5f),
        new Color(0.02f, 0.03f, 0.04f, 1f),
        new Color(1f, 0.2f, 0f, 0.25f),
        new Color(0.657f, 0.669f, 0.7f, 0.75f),
        new Color(0.681f, 0.3f, 0.8f, 1f),
        new Color(1.25f, -0.1f, 0.5f, 1f),
        new Color(0.25f, 0.75f, 0.35f, 0f),
        new Color(0.35f, 0.95f, 0.15f, 0.6f),
    };

    /// <summary>The quads' centers in the camera's view (orthographic, two units high): a grid
    /// of three by three cells, from the bottom left, row by row.</summary>
    static readonly Vector2[] Places = Grid();

    static Vector2[] Grid()
    {
        var places = new Vector2[9];
        for (int i = 0; i < places.Length; i++) places[i] = new Vector2((i % 3 - 1) * Cell, (i / 3 - 1) * Cell);
        return places;
    }

    static readonly Color MaterialColor = new Color(0.5f, 0.8f, 0.3f, 0.8f);
    static readonly Color TilemapColor = new Color(0.5f, 0.7f, 0.9f, 0.6f);

    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<(string Key, Camera Camera)> m_Stages = new List<(string, Camera)>();
    readonly List<UnityEngine.Object> m_Made = new List<UnityEngine.Object>();
    Texture2D m_White;
    Sprite m_Sprite;
    Material m_Raw, m_UrpRaw;
    string m_Pipeline;
    bool m_Urp;
    int m_Renderer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("VertexColorProbe").AddComponent<VertexColorProbe>();

    static string F(float f) => f.ToString("0.000000", CultureInfo.InvariantCulture);

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");

    IEnumerator Start()
    {
        yield return null;
        int code = 0;
        string dir = Environment.GetEnvironmentVariable("VERTEX_COLORS_OUT");
        var previous = GraphicsSettings.defaultRenderPipeline;
        m_White = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "white", filterMode = FilterMode.Point };
        var pixels = new Color32[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
        m_White.SetPixels32(pixels);
        m_White.Apply();
        m_Sprite = Sprite.Create(m_White, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 5f);
        m_Raw = new Material(Shader.Find("Probe/VertexColor")) { name = "raw" };
        m_UrpRaw = new Material(Shader.Find("Probe/UrpVertexColor")) { name = "urpraw" };
        Rec("colorspace", "\"" + QualitySettings.activeColorSpace + "\"");
        var urp = Resources.Load<UniversalRenderPipelineAsset>("Settings/ProbeURP");
        bool batcher = urp.useSRPBatcher;
        var phases = new (string Name, RenderPipelineAsset Asset, int Renderer, bool Batcher)[]
        {
            ("builtin", null, 0, batcher), ("universal", urp, UniversalRenderer, true), ("2d", urp, Renderer2D, true),
            ("nobatcher", urp, UniversalRenderer, false),
        };
        foreach (var phase in phases)
        {
            m_Pipeline = phase.Name;
            m_Urp = phase.Asset != null;
            m_Renderer = phase.Renderer;
            urp.useSRPBatcher = phase.Batcher;
            GraphicsSettings.defaultRenderPipeline = phase.Asset;
            yield return null;
            Build();
            for (int i = 0; i < Settle; i++) yield return null;
            yield return new WaitForEndOfFrame();
            try { Read(); }
            catch (Exception e)
            {
                Debug.LogError("vertex color probe: reading " + m_Pipeline + " failed: " + e);
                code = 1;
            }
            Clear();
        }
        GraphicsSettings.defaultRenderPipeline = previous;
        urp.useSRPBatcher = batcher;
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        if (string.IsNullOrEmpty(dir)) Debug.Log(m_Out.ToString());
        else
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    Material Stock()
    {
        var shader = Shader.Find(m_Urp ? "Universal Render Pipeline/2D/Sprite-Unlit-Default" : "Sprites/Default");
        var material = new Material(shader) { name = "stock" };
        m_Made.Add(material);
        return material;
    }

    void Build()
    {
        int index = 0;
        void Case(string name, Action<Transform> make)
        {
            var origin = new Vector3(100f * index++, 0f, 0f);
            var root = new GameObject(m_Pipeline + " " + name).transform;
            root.position = origin;
            var eye = new GameObject("eye " + name);
            eye.transform.SetParent(root, false);
            eye.transform.localPosition = new Vector3(0f, 0f, -10f);
            var camera = eye.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 1f;
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 20f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.targetTexture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { name = name };
            if (m_Urp)
            {
                var data = camera.GetUniversalAdditionalCameraData();
                data.SetRenderer(m_Renderer);
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
            }
            try { make(root); }
            catch (Exception e)
            {
                Debug.LogError("vertex color probe: " + m_Pipeline + " " + name + " failed: " + e);
                Rec(m_Pipeline + "." + name + ".failed", "\"" + e.GetType().Name + "\"");
                return;
            }
            m_Stages.Add((m_Pipeline + "." + name, camera));
        }

        Case("sprite.stock", root => Sprites(root, Stock()));
        Case("sprite.material", root =>
        {
            var material = Stock();
            material.color = MaterialColor;
            Sprites(root, material);
        });
        Case("sprite.raw", root => Sprites(root, m_Raw));
        // URP's include declares unity_SpriteColor; this shader does not read it.
        if (m_Urp) Case("sprite.urpraw", root => Sprites(root, m_UrpRaw));
        Case("tilemap.chunk", root => Tiles(root, Stock(), TilemapRenderer.Mode.Chunk, Color.white));
        Case("tilemap.individual", root => Tiles(root, Stock(), TilemapRenderer.Mode.Individual, Color.white));
        Case("tilemap.tinted", root => Tiles(root, Stock(), TilemapRenderer.Mode.Chunk, TilemapColor));
        Case("tilemap.individualtinted", root => Tiles(root, Stock(), TilemapRenderer.Mode.Individual, TilemapColor));
        Case("tilemap.raw", root => Tiles(root, m_Raw, TilemapRenderer.Mode.Chunk, Color.white));
        if (m_Urp) Case("tilemap.urprawindividual", root => Tiles(root, m_UrpRaw, TilemapRenderer.Mode.Individual, Color.white));
        if (m_Urp) Case("spriteshape.urpraw", root => Shapes(root, m_UrpRaw, false));
        Case("spriteshape.stock", root => Shapes(root, Stock(), false));
        Case("spriteshape.raw", root => Shapes(root, m_Raw, false));
        Case("spriteshape.channels", root => Shapes(root, m_Raw, true));
        Case("line.raw", root => Lines(root, false));
        Case("trail.raw", root => Lines(root, true));
        Case("particles.start", root => Particles(root, false));
        Case("particles.lifetime", root => Particles(root, true));
        Case("mesh.colors", root => Meshes(root, false));
        Case("mesh.colors32", root => Meshes(root, true));
        Case("ui.image", root => Images(root));
    }

    GameObject Child(Transform root, string name, int i)
    {
        var go = new GameObject(name + " " + i);
        go.transform.SetParent(root, false);
        go.transform.localPosition = Places[i];
        return go;
    }

    /// <summary>Sprites of a white texture, one unit square (scaled to half a unit), tinted by their renderers.</summary>
    void Sprites(Transform root, Material material)
    {
        for (int i = 0; i < Tints.Length; i++)
        {
            var go = Child(root, "sprite", i);
            go.transform.localScale = Vector3.one * Quad;
            var r = go.AddComponent<SpriteRenderer>();
            if (i == 0 && material.name == "stock" && material.color == Color.white)
                Rec(m_Pipeline + ".sprite.defaultmaterials", "\"" + Names(r.sharedMaterials) + "\"");
            r.sprite = m_Sprite;
            r.sharedMaterial = material;
            r.color = Tints[i];
        }
    }

    /// <summary>A tilemap of one white tile, half a unit square, at the grid's cells (two thirds of
    /// a unit), each cell tinted (Tilemap.SetColor).</summary>
    void Tiles(Transform root, Material material, TilemapRenderer.Mode mode, Color tilemapColor)
    {
        var grid = new GameObject("grid");
        grid.transform.SetParent(root, false);
        grid.transform.localPosition = new Vector3(-Cell / 2f, -Cell / 2f, 0f);
        grid.AddComponent<Grid>().cellSize = new Vector3(Cell, Cell, 1f);
        var holder = new GameObject("tilemap");
        holder.transform.SetParent(grid.transform, false);
        var tilemap = holder.AddComponent<Tilemap>();
        var renderer = holder.AddComponent<TilemapRenderer>();
        if (mode == TilemapRenderer.Mode.Chunk && material != m_Raw && tilemapColor == Color.white)
            Rec(m_Pipeline + ".tilemap.defaultmaterials", "\"" + Names(renderer.sharedMaterials) + "\"");
        renderer.mode = mode;
        renderer.sharedMaterial = material;
        tilemap.color = tilemapColor;
        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = Sprite.Create(m_White, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f / Quad);
        tile.flags = TileFlags.None;
        m_Made.Add(tile);
        for (int i = 0; i < Tints.Length; i++)
        {
            var cell = new Vector3Int(i % 3 - 1, i / 3 - 1, 0);
            tilemap.SetTile(cell, tile);
            tilemap.SetColor(cell, Tints[i]);
        }
    }

    /// <summary>Sprite shape renderers given a filled square each (the fill drawn with the white
    /// texture), tinted by the renderer's color, without a color channel as the sprite shape
    /// package writes them; or white renderers whose vertices carry the tint in a color channel.</summary>
    void Shapes(Transform root, Material material, bool channels)
    {
        for (int i = 0; i < Tints.Length; i++)
        {
            var go = Child(root, "shape", i);
            var r = go.AddComponent<SpriteShapeRenderer>();
            if (i == 0 && !channels && material.name == "stock") Rec(m_Pipeline + ".spriteshape.defaultmaterials", "\"" + Names(r.sharedMaterials) + "\"");
            // One material: a renderer draws its last submesh again with each material past its
            // submeshes, as the defaults would be here.
            r.sharedMaterials = new[] { material };
            r.color = channels ? Color.white : Tints[i];
            var segments = r.GetSegments(1);
            segments[0] = new SpriteShapeSegment { geomIndex = 0, indexCount = 6, vertexCount = 4, spriteIndex = -1 };
            NativeArray<ushort> indices;
            NativeSlice<Vector3> vertices;
            NativeSlice<Vector2> uvs;
            if (channels)
            {
                r.GetChannels(6, out indices, out vertices, out uvs, out NativeSlice<Color32> colors);
                for (int v = 0; v < 4; v++) colors[v] = Tints[i];
            }
            else r.GetChannels(6, out indices, out vertices, out uvs);
            vertices[0] = new Vector3(-Half, -Half, 0f);
            vertices[1] = new Vector3(Half, -Half, 0f);
            vertices[2] = new Vector3(-Half, Half, 0f);
            vertices[3] = new Vector3(Half, Half, 0f);
            uvs[0] = new Vector2(0f, 0f);
            uvs[1] = new Vector2(1f, 0f);
            uvs[2] = new Vector2(0f, 1f);
            uvs[3] = new Vector2(1f, 1f);
            indices[0] = 0; indices[1] = 2; indices[2] = 1;
            indices[3] = 1; indices[4] = 2; indices[5] = 3;
            r.SetLocalAABB(new Bounds(Vector3.zero, new Vector3(Quad, Quad, 0.1f)));
            var parameters = new SpriteShapeParameters
            {
                transform = go.transform.localToWorldMatrix,
                fillTexture = m_White,
                fillScale = 1,
                splineDetail = 4,
                angleThreshold = 30f,
                borderPivot = 0f,
                bevelCutoff = 0f,
                bevelSize = 0f,
                carpet = true,
                smartSprite = false,
                adaptiveUV = false,
                spriteBorders = false,
                stretchUV = false,
            };
            r.Prepare(default, parameters, new Sprite[0]);
        }
    }

    static string Names(Material[] materials)
    {
        var names = new string[materials.Length];
        for (int i = 0; i < materials.Length; i++) names[i] = materials[i] == null ? "null" : materials[i].name;
        return string.Join(",", names);
    }

    /// <summary>Line (or trail) renderers half a unit wide and long, of a solid color gradient.</summary>
    void Lines(Transform root, bool trail)
    {
        for (int i = 0; i < Tints.Length; i++)
        {
            var go = Child(root, trail ? "trail" : "line", i);
            var center = go.transform.position;
            var points = new[] { center + new Vector3(-Half, 0f, 0f), center + new Vector3(Half, 0f, 0f) };
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Tints[i], 0f), new GradientColorKey(Tints[i], 1f) },
                new[] { new GradientAlphaKey(Tints[i].a, 0f), new GradientAlphaKey(Tints[i].a, 1f) });
            if (trail)
            {
                // The trail's head is its transform: at the right end, with the left end added.
                go.transform.position = points[1];
                var r = go.AddComponent<TrailRenderer>();
                r.sharedMaterial = m_Raw;
                r.time = 1000f;
                r.minVertexDistance = 0.01f;
                r.widthMultiplier = Quad;
                r.colorGradient = gradient;
                r.AddPosition(points[0]);
            }
            else
            {
                var r = go.AddComponent<LineRenderer>();
                r.sharedMaterial = m_Raw;
                r.useWorldSpace = true;
                r.widthMultiplier = Quad;
                r.colorGradient = gradient;
                r.positionCount = 2;
                r.SetPositions(points);
            }
        }
    }

    /// <summary>One still particle half a unit in size per system: its start color the tint, or white
    /// with the tint as a constant color over its lifetime.</summary>
    void Particles(Transform root, bool overLifetime)
    {
        for (int i = 0; i < Tints.Length; i++)
        {
            var go = Child(root, "particles", i);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 100f;
            main.startLifetime = 100f;
            main.startSpeed = 0f;
            main.startSize = Quad;
            main.maxParticles = 1;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = overLifetime ? Color.white : Tints[i];
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            if (overLifetime)
            {
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Tints[i], 0f), new GradientColorKey(Tints[i], 1f) },
                    new[] { new GradientAlphaKey(Tints[i].a, 0f), new GradientAlphaKey(Tints[i].a, 1f) });
                var col = ps.colorOverLifetime;
                col.enabled = true;
                col.color = new ParticleSystem.MinMaxGradient(gradient);
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m_Raw;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            ps.Emit(1);
        }
    }

    /// <summary>Mesh renderers of a quad half a unit square whose vertices carry the tint (Mesh.colors or
    /// Mesh.colors32): mesh data the renderer passes on as it is.</summary>
    void Meshes(Transform root, bool bytes)
    {
        for (int i = 0; i < Tints.Length; i++)
        {
            var go = Child(root, "mesh", i);
            var mesh = new Mesh { name = "quad " + i };
            mesh.vertices = new[] { new Vector3(-Half, -Half, 0f), new Vector3(Half, -Half, 0f), new Vector3(-Half, Half, 0f), new Vector3(Half, Half, 0f) };
            if (bytes)
            {
                Color32 c = Tints[i];
                mesh.colors32 = new[] { c, c, c, c };
            }
            else mesh.colors = new[] { Tints[i], Tints[i], Tints[i], Tints[i] };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m_Made.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m_Raw;
        }
    }

    /// <summary>uGUI images of the tints on a world-space canvas (the default UI material).</summary>
    void Images(Transform root)
    {
        var holder = new GameObject("canvas");
        holder.transform.SetParent(root, false);
        var canvas = holder.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = root.GetComponentInChildren<Camera>();
        var rect = (RectTransform)holder.transform;
        rect.sizeDelta = new Vector2(2f, 2f);
        rect.localPosition = Vector3.zero;
        for (int i = 0; i < Tints.Length; i++)
        {
            var go = new GameObject("image " + i, typeof(RectTransform));
            go.transform.SetParent(holder.transform, false);
            var r = (RectTransform)go.transform;
            r.sizeDelta = new Vector2(Quad, Quad);
            r.localPosition = Places[i];
            go.AddComponent<Image>().color = Tints[i];
        }
    }

    void Read()
    {
        var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        foreach (var (key, camera) in m_Stages)
        {
            RenderTexture.active = camera.targetTexture;
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply();
            for (int i = 0; i < Places.Length; i++)
            {
                int x = (i % 3) * Size / 3 + Size / 6, y = (i / 3) * Size / 3 + Size / 6;
                var c = image.GetPixel(x, y);
                Rec(key + "." + i + ".r", F(c.r));
                Rec(key + "." + i + ".g", F(c.g));
                Rec(key + "." + i + ".b", F(c.b));
                Rec(key + "." + i + ".a", F(c.a));
            }
        }
        RenderTexture.active = null;
        Destroy(image);
    }

    void Clear()
    {
        foreach (var (_, camera) in m_Stages)
        {
            var target = camera.targetTexture;
            camera.targetTexture = null;
            Destroy(target);
            Destroy(camera.transform.parent.gameObject);
        }
        m_Stages.Clear();
        foreach (var o in m_Made) Destroy(o);
        m_Made.Clear();
    }
}
