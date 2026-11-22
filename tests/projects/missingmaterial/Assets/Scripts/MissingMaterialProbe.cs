using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Renderers whose material is missing, under the Universal Render Pipeline and then
/// the built-in one: what they draw, whether they hide what is behind them and reach the
/// camera's depth texture. Each scenario's image is written into MISSING_MATERIAL_OUT
/// (and values.jsonl), then the run exits. See README.md.</summary>
public class MissingMaterialProbe : MonoBehaviour
{
    const int Width = 320, Height = 180;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("MissingMaterialProbe").AddComponent<MissingMaterialProbe>();

    readonly StringBuilder m_Out = new StringBuilder();
    string m_Dir;

    void Value(string key, string json) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(json).Append("}\n");

    static GameObject Shape(PrimitiveType type, string name, Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.position = position;
        go.transform.localScale = scale;
        return go;
    }

    /// <summary>Two unit quads side by side, one sub-mesh each.</summary>
    static Mesh TwoQuads()
    {
        var mesh = new Mesh { name = "TwoQuads" };
        mesh.vertices = new[]
        {
            new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(-1f, 1f, 0f),
            new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f),
        };
        mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        mesh.subMeshCount = 2;
        mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        mesh.SetTriangles(new[] { 4, 6, 5, 4, 7, 6 }, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    IEnumerator Start()
    {
        m_Dir = Environment.GetEnvironmentVariable("MISSING_MATERIAL_OUT");
        foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None)) renderer.enabled = false;
        foreach (var other in FindObjectsByType<Light>(FindObjectsSortMode.None)) other.enabled = false;
        var asset = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        asset.renderScale = 1f;
        asset.upscalingFilter = UpscalingFilterSelection.Linear;
        var camera = Camera.main;
        var data = camera.GetUniversalAdditionalCameraData();
        data.antialiasing = AntialiasingMode.None;
        data.requiresDepthTexture = true;
        // The depth texture as it is after the opaque objects, where a transparent one reads it
        // (UniversalRenderPipelineAsset exposes its renderers' data only internally).
        var rendererData = (UniversalRendererData)((ScriptableRendererData[])typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(asset))[0];
        rendererData.copyDepthMode = CopyDepthMode.AfterOpaques;
        camera.depthTextureMode = DepthTextureMode.Depth;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.1f, 0.2f, 0.4f, 1f);
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 100f;
        camera.fieldOfView = 60f;
        camera.transform.SetPositionAndRotation(new Vector3(0f, 2f, -7f), Quaternion.Euler(12f, 0f, 0f));
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.shadows = LightShadows.Hard;
        sun.intensity = 1f;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // The lit ground, and a red ball half behind the cube without a material.
        var ground = Shape(PrimitiveType.Plane, "Ground", Vector3.zero, new Vector3(2f, 1f, 2f)).GetComponent<MeshRenderer>();
        var ball = Shape(PrimitiveType.Sphere, "Ball", new Vector3(-2.2f, 1f, 1.5f), Vector3.one * 1.6f).GetComponent<MeshRenderer>();
        var missing = Shape(PrimitiveType.Cube, "Missing", new Vector3(-3f, 1f, 0f), Vector3.one * 1.4f).GetComponent<MeshRenderer>();
        missing.sharedMaterial = null;
        // Two sub-meshes: the first with a material, the second without.
        var halves = new GameObject("Halves", typeof(MeshFilter), typeof(MeshRenderer));
        halves.GetComponent<MeshFilter>().sharedMesh = TwoQuads();
        halves.transform.position = new Vector3(0f, 0.6f, 0f);
        var halvesRenderer = halves.GetComponent<MeshRenderer>();
        // No material slot at all.
        var none = Shape(PrimitiveType.Cube, "NoSlots", new Vector3(0f, 2.6f, 0f), Vector3.one).GetComponent<MeshRenderer>();
        none.sharedMaterials = new Material[0];
        // The error shader named by a material, as a control.
        var named = Shape(PrimitiveType.Cube, "Named", new Vector3(3f, 1f, 0f), Vector3.one * 1.4f).GetComponent<MeshRenderer>();
        named.sharedMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
        // A sprite without a material.
        var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        var texels = new Color32[64];
        for (int i = 0; i < 64; i++) texels[i] = ((i % 8) + (i / 8)) % 2 == 0 ? new Color32(255, 255, 0, 255) : new Color32(0, 255, 255, 255);
        texture.SetPixels32(texels);
        texture.filterMode = FilterMode.Point;
        texture.Apply();
        var sprite = new GameObject("Sprite").AddComponent<SpriteRenderer>();
        sprite.sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8f);
        sprite.transform.position = new Vector3(1.6f, 2.6f, 0f);
        sprite.sharedMaterial = null;
        // A skinned mesh and a line without a material.
        var skinned = new GameObject("Skinned").AddComponent<SkinnedMeshRenderer>();
        skinned.sharedMesh = missing.GetComponent<MeshFilter>().sharedMesh;
        skinned.transform.position = new Vector3(-1.5f, 2.6f, 0f);
        skinned.transform.localScale = Vector3.one * 0.8f;
        skinned.sharedMaterial = null;
        var line = new GameObject("Line").AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.SetPosition(0, new Vector3(-4.2f, 2.4f, 0f));
        line.SetPosition(1, new Vector3(-2.6f, 3.2f, 0f));
        line.startWidth = line.endWidth = 0.2f;
        line.sharedMaterial = null;
        // One sub-mesh under two slots, the second without a material. The first is translucent
        // (drawn after the opaque objects), so that the image tells whether the second slot draws
        // the sub-mesh again without depending on which of two draws at one depth comes first.
        var extra = Shape(PrimitiveType.Cube, "Extra", new Vector3(1.6f, 0.5f, -1.6f), Vector3.one * 0.8f).GetComponent<MeshRenderer>();
        // A material destroyed while its renderer keeps it.
        var destroyed = Shape(PrimitiveType.Cube, "Destroyed", new Vector3(3.2f, 2.9f, 0f), Vector3.one * 0.8f).GetComponent<MeshRenderer>();
        var doomed = new Material(Shader.Find("Hidden/InternalErrorShader"));
        destroyed.sharedMaterial = doomed;
        DestroyImmediate(doomed);
        // Text of the built-in font whose renderer has no material.
        var text = new GameObject("Text", typeof(MeshRenderer)).AddComponent<TextMesh>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = "Ab";
        text.characterSize = 0.25f;
        text.fontSize = 40;
        text.anchor = TextAnchor.MiddleCenter;
        text.transform.position = new Vector3(-1.2f, 0.6f, -2f);
        text.GetComponent<MeshRenderer>().sharedMaterial = null;
        // The camera's depth texture over the whole view (the depth scenarios).
        var depthView = Shape(PrimitiveType.Quad, "DepthView", Vector3.zero, Vector3.one * 4f);
        depthView.transform.SetParent(camera.transform, false);
        depthView.transform.localPosition = new Vector3(0f, 0f, 1f);
        depthView.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Probe/DepthView"));
        depthView.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        var veil = new Material(Shader.Find("Sprites/Default"));
        veil.SetColor("_Color", new Color(1f, 0f, 0f, 0.5f));
        foreach (string pipeline in new[] { "urp", "builtin" })
        {
            bool urp = pipeline == "urp";
            if (!urp)
            {
                GraphicsSettings.defaultRenderPipeline = null;
                QualitySettings.renderPipeline = null;
            }
            var lit = new Material(Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard"));
            lit.SetColor(urp ? "_BaseColor" : "_Color", new Color(0.7f, 0.7f, 0.7f, 1f));
            var red = new Material(Shader.Find(urp ? "Universal Render Pipeline/Unlit" : "Unlit/Color"));
            red.SetColor(urp ? "_BaseColor" : "_Color", Color.red);
            ground.sharedMaterial = lit;
            ball.sharedMaterial = red;
            halvesRenderer.sharedMaterials = new[] { red, null };
            extra.sharedMaterials = new[] { veil, null };
            foreach (bool depth in new[] { false, true })
            {
                depthView.SetActive(depth);
                // A few frames: the pipeline's first frames after a change settle its targets.
                for (int i = 0; i < 4; i++) yield return null;
                string name = pipeline + (depth ? "_depth" : "_scene");
                Value(name + ".pipeline", "\"" + (GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.GetType().Name : "none") + "\"");
                Record(target, name);
            }
        }
        Value("missing.material", missing.sharedMaterial == null ? "\"null\"" : "\"" + missing.sharedMaterial.shader.name + "\"");
        Value("destroyed.material", destroyed.sharedMaterial == null ? "\"null\"" : "\"alive\"");
        Value("sprite.material", sprite.sharedMaterial == null ? "\"null\"" : "\"" + sprite.sharedMaterial.shader.name + "\"");
        Value("complete", "true");
        int code = 0;
        if (string.IsNullOrEmpty(m_Dir)) { Debug.LogError("MISSING_MATERIAL_OUT is not set"); code = 1; }
        else File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    void Record(RenderTexture target, string name)
    {
        if (string.IsNullOrEmpty(m_Dir)) return;
        var read = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        read.Apply();
        RenderTexture.active = previous;
        Directory.CreateDirectory(m_Dir);
        File.WriteAllBytes(Path.Combine(m_Dir, name + ".png"), read.EncodeToPNG());
        Destroy(read);
    }
}
