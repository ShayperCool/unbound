// The probe runs in the Editor only (it loads its graphs through the asset database).
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;

/// <summary>
/// Draws the probe's effects, each in front of a camera of its own under the probe's lights,
/// and saves the frames named as images with a row of the effect's state (see README.md):
/// into VFX_PROBE_OUT, then exits Play mode.
/// </summary>
public sealed class LitProbe : MonoBehaviour
{
    const int k_LastFrame = 26;
    /// <summary>The pipeline asset's second renderer: the deferred one.</summary>
    const int k_DeferredRenderer = 1;
    /// <summary>The pipeline asset's third renderer: the 2D one.</summary>
    const int k_2DRenderer = 2;
    /// <summary>The pipeline asset's fourth renderer: the forward one with the render
    /// pipeline's decal feature (its technique chosen for the device: screen space on OpenGL
    /// Core).</summary>
    const int k_DecalRenderer = 3;

    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<Stage> m_Stages = new List<Stage>();
    Texture2D m_Shot;
    int m_Frame;
    bool m_Done;

    /// <summary>An effect far from the others, in front of an orthographic camera of its own
    /// that renders into a small texture.</summary>
    sealed class Stage
    {
        public string Key;
        public VisualEffect Effect;
        public Camera Camera;
        public Vector3 Origin;
        public int[] Frames;
    }

    Stage m_Exposed;
    Stage m_Sorting;
    Stage m_MeshSorted;
    Stage m_MeshPaused;
    int m_Staged;
    Material m_Lit;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Time.captureFramerate = 30;
        new GameObject("LitProbe").AddComponent<LitProbe>();
    }

    static string Escape(string s)
    {
        var b = new StringBuilder();
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\n");
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.ToString();
    }

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(value ?? "<null>")).Append("\"}\n");

    void Start()
    {
        try
        {
            // One light for every stage: from behind the cameras, from above and the left. It
            // casts shadows: of what a stage has that casts any.
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.9f);
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 1f;
            sun.transform.rotation = Quaternion.Euler(35f, 25f, 0f);
            m_Lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m_Lit.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f, 1f));

            Draw("matte", "LitMatte", 5);
            Lamp(Draw("smooth", "LitSmooth", 5), LightType.Point);
            Lamp(Draw("metal", "LitMetal", 5), LightType.Spot);
            Draw("additive", "LitAdditive", 5);
            Lamp(Draw("mapped", "LitMapped", 5), LightType.Point);
            Lamp(Draw("outputs", "LitOutputs", 5), LightType.Point);
            // The same graph under a box that casts the sun's shadow on its quads.
            Shade(Draw("shadowed", "LitMatte", 5), sun);
            // Quads and meshes that cast shadows themselves, in front of a wall.
            Wall(Draw("caster", "LitCaster", 5));
            Wall(Draw("mesh caster", "MeshCaster", 5));
            Wall(Draw("unlit caster", "UnlitCaster", 5));
            // Casters the camera does not see: up the sun's rays from the stage, their shadows
            // fall on the wall in view.
            var offscreen = Draw("offscreen caster", "LitCaster", 5, 12);
            Wall(offscreen);
            offscreen.Effect.transform.position = offscreen.Origin - sun.transform.forward * 6f;
            // And casters far from every camera and from every shadow: rows alone.
            Watch("hidden caster", "LitCaster", new Vector3(0f, 5000f, 0f), 5, 12);
            Watch("hidden", "LitMatte", new Vector3(100f, 5000f, 0f), 5, 12);
            // No effect: a box of the scene's that casts and receives the sun's shadows before
            // such a wall, under such a box. What differs here differs without any effect.
            var plain = Draw("plain shadow", null, 5);
            Wall(plain);
            Shade(plain, sun);
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "block";
            Destroy(block.GetComponent<Collider>());
            block.transform.position = plain.Origin + new Vector3(-0.3f, -0.2f, 0f);
            block.transform.rotation = Quaternion.Euler(25f, 40f, 10f);
            block.GetComponent<MeshRenderer>().sharedMaterial = m_Lit;
            // Through the deferred renderer.
            var deferred = Lamp(Draw("deferred", "LitMatte", 5), LightType.Point);
            deferred.Camera.GetUniversalAdditionalCameraData().SetRenderer(k_DeferredRenderer);
            var deferredMesh = Lamp(Draw("mesh deferred", "MeshLit", 5), LightType.Point);
            deferredMesh.Camera.GetUniversalAdditionalCameraData().SetRenderer(k_DeferredRenderer);

            Draw("mesh unlit", "MeshUnlit", 5);
            Lamp(Draw("mesh lit", "MeshLit", 5), LightType.Point);
            m_Exposed = Draw("mesh exposed", "MeshExposed", 5, 8, 11, 14, 17, 20, 23);
            Draw("mesh none", "MeshNone", 5);
            Draw("mesh dying", "MeshDying", 12, 24);
            // Lit quads blended over what is behind them (sorted, through an indirect buffer),
            // and quads sized and coloured over their life by their output.
            Lamp(Draw("lit sorted", "LitSorted", 5, 12), LightType.Point);
            Draw("curve output", "CurveOutput", 6, 12, 24);
            // Through the 2D renderer, each at a place of its own where the scene's 2D lights
            // stand (LitAuthoring.Place2D): Shader Graph outputs of the render pipeline's
            // sprite graphs under the dim global light, a point light, a point light that
            // lights by normals; unlit; the lit graph given to a quad output of the kind
            // without one; and an output without a Shader Graph.
            Draw2D("sprite", "SpriteLit", 0, 5);
            Draw2D("sprite point", "SpriteLit", 1, 5);
            Draw2D("sprite normals", "SpriteLit", 2, 5);
            Draw2D("sprite unlit", "SpriteUnlit", 3, 5);
            Draw2D("sprite classic", "SpriteClassic", 4, 5);
            Draw2D("plain 2d", "PlainUnlit", 5, 5);
            // Unlit sprites before and after the effect by their sorting order, each where its
            // distance to the camera would put it the other way round; at frame 8 the effect's
            // renderer is ordered after both.
            m_Sorting = Draw2D("sprite sorting", "SpriteUnlit", 6, 5, 10);
            Sprite2D(m_Sorting, "behind", -1, new Vector3(-0.6f, -0.6f, -3f));
            Sprite2D(m_Sorting, "before", 1, new Vector3(0.6f, 0.6f, 3f));
            // A Shader Graph output (the graph package's default graph) through the forward
            // renderer.
            Draw("graph unlit", "GraphUnlit", 5);
            // Meshes through an indirect buffer: sorted (unlit, its mesh changed at frame 8 to
            // one of a single submesh; and lit, with a point light), and from a list that is not
            // sorted.
            m_MeshSorted = Draw("mesh sorted", "MeshSorted", 5, 10);
            Lamp(Draw("mesh lit sorted", "MeshLitSorted", 5), LightType.Point);
            Draw("mesh listed", "MeshListed", 5);
            // The sorted meshes paused at frame 6, their mesh changed at frame 8, drawn at frame
            // 10.
            m_MeshPaused = Draw("mesh paused", "MeshSorted", 5, 10);
            // Lit quads from a list that is not sorted (opaque, told to draw through an indirect
            // buffer), under a point light; the sprite graph's quads blended over what is behind
            // them with their sort off, through the 2D renderer under a 2D point light; meshes
            // of an output of two by the particles' mesh index (and by their size on screen); a
            // quad output drawn by a graph of the older Visual Effect target; and lit quads and
            // meshes through the 2D renderer.
            Lamp(Draw("lit listed", "LitListed", 5, 12), LightType.Point);
            Draw2D("sprite listed", "SpriteListed", 7, 5, 12);
            Draw("mesh pair", "MeshPair", 5);
            // Meshes chosen by their size on screen: which the Editor draws differs from one run
            // to the next (two captures disagree), so no image of them is kept; the stage keeps
            // its place and its renderer's row.
            Draw("mesh lod", "MeshLod");
            Draw("graph legacy", "GraphLegacy", 5);
            Draw2D("lit 2d", "LitMatte", 8, 5);
            Draw2D("mesh 2d", "MeshUnlit", 9, 5);
            // Meshes drawn by a graph of the older target that samples its node's default
            // texture.
            Draw("graph legacy mesh", "GraphLegacyMesh", 5);
            // Quads drawn by a lit graph of the render pipeline (its passes include the
            // lightmapper's, which no camera draws), under a point light.
            Lamp(Draw("graph lit", "GraphLit", 5), LightType.Point);
            // Decals the particles project onto a wall, through the renderer with the decal
            // feature, under the sun and a point light.
            var decals = Lamp(Draw("decal", "LitDecal", 5, 12), LightType.Point);
            Wall(decals);
            decals.Camera.GetUniversalAdditionalCameraData().SetRenderer(k_DecalRenderer);
            // What each effect's renderer is set to: an asset whose graph has an output that
            // casts shadows asks its renderer to.
            foreach (var stage in m_Stages)
            {
                if (stage.Effect == null) continue;
                var renderer = stage.Effect.GetComponent<Renderer>();
                Rec("renderer/" + stage.Key, renderer == null ? "none" : "cast=" + renderer.shadowCastingMode + " receive=" + (renderer.receiveShadows ? 1 : 0)
                    + " motion=" + renderer.motionVectorGenerationMode + " probes=" + renderer.lightProbeUsage + " reflections=" + renderer.reflectionProbeUsage
                    + " materials=" + string.Join(", ", renderer.sharedMaterials.Select(m => m == null ? "<null>" : m.name)));
            }
            // An effect without an asset, and one given an asset and then none.
            var bare = new GameObject("bare").AddComponent<VisualEffect>();
            bare.transform.position = new Vector3(200f, 5000f, 0f);
            RecRenderer("renderer/no asset", bare);
            bare.visualEffectAsset = Graph("LitCaster");
            RecRenderer("renderer/asset given", bare);
            bare.visualEffectAsset = null;
            RecRenderer("renderer/asset taken", bare);
            // An asset with an output that writes motion vectors, and one saved with renderer
            // settings of its own.
            bare.visualEffectAsset = Graph("LitMoving");
            RecRenderer("renderer/motion vectors", bare);
            bare.visualEffectAsset = Graph("LitReceiving");
            RecRenderer("renderer/saved settings", bare);
            bare.visualEffectAsset = null;

            // Setters given nothing, or an object that is no more.
            var nulls = new GameObject("nulls").AddComponent<VisualEffect>();
            nulls.transform.position = new Vector3(300f, 5000f, 0f);
            nulls.visualEffectAsset = Graph("MeshExposed");
            nulls.SetMesh("Shape", LoadMesh("Spike"));
            Throws("null/SetMesh", () => nulls.SetMesh("Shape", null));
            Throws("null/SetMesh unknown name", () => nulls.SetMesh("Nothing", null));
            Throws("null/SetTexture", () => nulls.SetTexture("Nothing", null));
            Throws("null/SetAnimationCurve", () => nulls.SetAnimationCurve("Nothing", null));
            Throws("null/SetGradient", () => nulls.SetGradient("Nothing", null));
            var goneMesh = Instantiate(LoadMesh("Spike"));
            DestroyImmediate(goneMesh);
            Throws("null/SetMesh destroyed", () => nulls.SetMesh("Shape", goneMesh));
            var goneTexture = new Texture2D(2, 2);
            DestroyImmediate(goneTexture);
            Throws("null/SetTexture destroyed", () => nulls.SetTexture("Nothing", goneTexture));
            var kept = nulls.GetMesh("Shape");
            Rec("null/Shape after", kept == null ? "<null>" : kept.name);
            StartCoroutine(EndOfFrames());
        }
        catch (Exception e)
        {
            Rec("failed", e.ToString());
            Debug.LogError("vfxlit probe failed: " + e);
            Finish();
        }
    }

    static VisualEffectAsset Graph(string name) => AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(LitAuthoringPaths.GraphFolder + "/" + name + ".vfx");

    /// <summary>Records what a call throws: the exception's type, and the parameter it names.</summary>
    void Throws(string key, Action call)
    {
        try
        {
            call();
            Rec(key, "nothing");
        }
        catch (Exception e)
        {
            Rec(key, e.GetType().Name + (e is ArgumentException argument ? " " + argument.ParamName : ""));
        }
    }

    /// <summary>An effect no camera of the probe looks at: its rows alone are recorded.</summary>
    void Watch(string key, string graph, Vector3 position, params int[] frames)
    {
        var go = new GameObject("watch " + key);
        go.transform.position = position;
        var effect = go.AddComponent<VisualEffect>();
        effect.resetSeedOnPlay = false;
        effect.startSeed = 11;
        effect.visualEffectAsset = Graph(graph);
        m_Stages.Add(new Stage { Key = key, Effect = effect, Origin = position, Frames = frames });
    }

    void RecRenderer(string key, VisualEffect effect)
    {
        var renderer = effect.GetComponent<Renderer>();
        Rec(key, renderer == null ? "none" : "cast=" + renderer.shadowCastingMode + " receive=" + (renderer.receiveShadows ? 1 : 0) + " motion=" + renderer.motionVectorGenerationMode
            + " materials=" + renderer.sharedMaterials.Length);
    }

    /// <summary>A stage for an effect of a graph: the frames named are saved.</summary>
    Stage Draw(string key, string graph, params int[] frames) => DrawAt(key, graph, new Vector3(1000f + 100f * m_Staged++, 0f, 0f), frames);

    /// <summary>A stage drawn through the 2D renderer at a place of the row the scene's 2D
    /// lights stand in (LitAuthoring.Place2D).</summary>
    Stage Draw2D(string key, string graph, int place, params int[] frames)
    {
        var stage = DrawAt(key, graph, new Vector3(1000f + 100f * place, 2000f, 0f), frames);
        stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(k_2DRenderer);
        return stage;
    }

    /// <summary>An unlit sprite of the image whose quarters differ, two units wide, untinted,
    /// at a place of the stage, in the default sorting layer at the order given.</summary>
    void Sprite2D(Stage stage, string name, int order, Vector3 offset)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Objects/Quarters.png");
        var go = new GameObject("sprite " + stage.Key + " " + name);
        go.transform.position = stage.Origin + offset;
        var sprite = go.AddComponent<SpriteRenderer>();
        sprite.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width / 2f);
        sprite.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        sprite.sortingOrder = order;
    }

    Stage DrawAt(string key, string graph, Vector3 origin, int[] frames)
    {
        VisualEffect effect = null;
        if (graph != null)
        {
            var go = new GameObject("draw " + key);
            go.transform.position = origin;
            effect = go.AddComponent<VisualEffect>();
            effect.resetSeedOnPlay = false;
            effect.startSeed = 11;
            effect.visualEffectAsset = Graph(graph);
        }
        var eye = new GameObject("eye " + key);
        eye.transform.position = origin + new Vector3(0f, 0f, -10f);
        var camera = eye.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 1.5f;
        camera.nearClipPlane = 1f;
        camera.farClipPlane = 20f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        camera.targetTexture = new RenderTexture(64, 64, 24);
        var stage = new Stage { Key = key, Effect = effect, Camera = camera, Origin = origin, Frames = frames };
        m_Stages.Add(stage);
        return stage;
    }

    /// <summary>A light of the stage's own beside its camera (its range ends long before the
    /// next stage).</summary>
    Stage Lamp(Stage stage, LightType type)
    {
        var lamp = new GameObject("lamp " + stage.Key).AddComponent<Light>();
        lamp.type = type;
        lamp.color = new Color(0.3f, 0.6f, 1f);
        lamp.intensity = type == LightType.Spot ? 8f : 3f;
        lamp.range = 6f;
        lamp.spotAngle = 50f;
        lamp.innerSpotAngle = 30f;
        lamp.shadows = LightShadows.None;
        lamp.transform.position = stage.Origin + new Vector3(1f, 0.8f, -2f);
        lamp.transform.rotation = Quaternion.LookRotation(stage.Origin + new Vector3(0.2f, 0.1f, 0f) - lamp.transform.position);
        return stage;
    }

    /// <summary>A box the camera does not draw, between the sun and the stage's particles: its
    /// shadow falls across them.</summary>
    void Shade(Stage stage, Light sun)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "shade " + stage.Key;
        Destroy(box.GetComponent<Collider>());
        box.transform.position = stage.Origin - sun.transform.forward * 3f + new Vector3(0.3f, 0.2f, 0f);
        box.transform.rotation = Quaternion.Euler(10f, 30f, 20f);
        box.transform.localScale = new Vector3(1.1f, 1.1f, 1.1f);
        var renderer = box.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = m_Lit;
        renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
    }

    /// <summary>A lit wall behind the stage's particles, facing the camera: shadows show on
    /// it.</summary>
    void Wall(Stage stage)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
        wall.name = "wall " + stage.Key;
        Destroy(wall.GetComponent<Collider>());
        wall.transform.position = stage.Origin + new Vector3(0f, 0f, 1.5f);
        wall.transform.localScale = new Vector3(4f, 4f, 1f);
        var renderer = wall.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = m_Lit;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
    }

    /// <summary>What the probe does to the effect whose mesh and mask are exposed, before the
    /// frame named is simulated: each is drawn two frames later.</summary>
    void Act()
    {
        if (m_Frame == 6)
        {
            m_MeshPaused.Effect.pause = true;
            Rec("act/mesh paused/f006", "pause=" + (m_MeshPaused.Effect.pause ? 1 : 0));
        }
        if (m_Frame == 8)
        {
            var renderer = m_Sorting.Effect.GetComponent<Renderer>();
            renderer.sortingOrder = 2;
            Rec("act/sprite sorting/f008", "sortingOrder=2 -> sortingLayer=" + renderer.sortingLayerName + " sortingOrder=" + renderer.sortingOrder);
            m_MeshSorted.Effect.SetMesh("Shape", LoadMesh("Spike"));
            var now = m_MeshSorted.Effect.GetMesh("Shape");
            Rec("act/mesh sorted/f008", "Shape=Spike -> Shape=" + (now == null ? "<null>" : now.name));
            m_MeshPaused.Effect.SetMesh("Shape", LoadMesh("Spike"));
            var paused = m_MeshPaused.Effect.GetMesh("Shape");
            Rec("act/mesh paused/f008", "Shape=Spike -> Shape=" + (paused == null ? "<null>" : paused.name));
        }
        var effect = m_Exposed.Effect;
        string what = null;
        switch (m_Frame)
        {
            // The first submesh alone, the second alone, then a mask that names the second and
            // submeshes the mesh does not have.
            case 6: effect.SetUInt("Mask", 1u); what = "Mask=1"; break;
            case 9: effect.SetUInt("Mask", 2u); what = "Mask=2"; break;
            case 12: effect.SetUInt("Mask", 0xfffffffeu); what = "Mask=0xfffffffe"; break;
            // Another mesh, of one submesh, under that mask (nothing of it is named) and under
            // one that names it.
            case 15: effect.SetMesh("Shape", LoadMesh("Spike")); what = "Shape=Spike"; break;
            case 18: effect.SetUInt("Mask", 0xffffffffu); what = "Mask=0xffffffff"; break;
            // No mesh cannot be set: the effect keeps the one it has. Then the first mesh again.
            case 20:
                try { effect.SetMesh("Shape", null); what = "Shape=null"; }
                catch (ArgumentNullException e) { what = "Shape=null throws ArgumentNullException " + e.ParamName; }
                break;
            case 21: effect.SetMesh("Shape", LoadMesh("Wedge")); what = "Shape=Wedge"; break;
        }
        if (what == null) return;
        var shape = effect.GetMesh("Shape");
        Rec("act/mesh exposed/f" + m_Frame.ToString("000"), what + " -> Shape=" + (shape == null ? "<null>" : shape.name) + " Mask=" + effect.GetUInt("Mask"));
    }

    static Mesh LoadMesh(string name) => AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Objects/" + name + ".asset");

    System.Collections.IEnumerator EndOfFrames()
    {
        var wait = new WaitForEndOfFrame();
        while (!m_Done)
        {
            yield return wait;
            Shoot();
        }
    }

    void Shoot()
    {
        foreach (var stage in m_Stages)
        {
            if (Array.IndexOf(stage.Frames, m_Frame) < 0) continue;
            string frame = "f" + m_Frame.ToString("000");
            if (stage.Effect == null) Rec("draw/" + stage.Key + "/" + frame, "no effect");
            else
            {
                var renderer = stage.Effect.GetComponent<Renderer>();
                Rec("draw/" + stage.Key + "/" + frame, "alive=" + stage.Effect.aliveParticleCount + " culled=" + (stage.Effect.culled ? 1 : 0)
                    + " awake=" + (stage.Effect.HasAnySystemAwake() ? 1 : 0)
                    + " renderer " + (renderer == null ? "none" : "enabled=" + (renderer.enabled ? 1 : 0) + " visible=" + (renderer.isVisible ? 1 : 0)));
            }
            var dir = Environment.GetEnvironmentVariable("VFX_PROBE_OUT");
            if (string.IsNullOrEmpty(dir) || stage.Camera == null) continue;
            if (m_Shot == null) m_Shot = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var active = RenderTexture.active;
            RenderTexture.active = stage.Camera.targetTexture;
            m_Shot.ReadPixels(new Rect(0f, 0f, 64f, 64f), 0, 0);
            m_Shot.Apply();
            RenderTexture.active = active;
            File.WriteAllBytes(Path.Combine(dir, "draw-" + stage.Key + "-" + frame + ".png"), m_Shot.EncodeToPNG());
        }
    }

    void Update()
    {
        if (m_Done) return;
        m_Frame++;
        try
        {
            Act();
        }
        catch (Exception e)
        {
            Rec("failed", e.ToString());
            Debug.LogError("vfxlit probe failed: " + e);
            Finish();
            return;
        }
        if (m_Frame < k_LastFrame) return;
        Finish();
    }

    /// <summary>The graphs imported while the probe plays. None must be: the capture lets the
    /// Editor settle before it plays (Assets/Editor/ProbeRunner.cs).</summary>
    sealed class ImportsWhilePlaying : AssetPostprocessor
    {
        public static readonly List<string> Graphs = new List<string>();

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!EditorApplication.isPlaying) return;
            foreach (var path in imported)
                if (path.EndsWith(".vfx", StringComparison.Ordinal)) Graphs.Add(path);
        }
    }

    void Finish()
    {
        m_Done = true;
        Rec("capture/graphs imported while playing", string.Join(", ", ImportsWhilePlaying.Graphs.OrderBy(p => p, StringComparer.Ordinal)));
        // A capture with such an import is not one to publish: it stays incomplete.
        if (ImportsWhilePlaying.Graphs.Count != 0) Debug.LogError("vfxlit probe: graphs were imported while playing; the capture is not complete");
        else m_Out.Append("{\"k\": \"complete\", \"v\": true}\n");
        var dir = Environment.GetEnvironmentVariable("VFX_PROBE_OUT");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        EditorApplication.ExitPlaymode();
        EditorApplication.Exit(0);
    }
}

/// <summary>Where the authoring step keeps the graphs (the authoring class itself is in an
/// editor assembly the probe's script does not see).</summary>
static class LitAuthoringPaths
{
    public const string GraphFolder = "Assets/Graphs";
}
#endif
