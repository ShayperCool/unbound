using UnityEngine;

// Projectors: Standard Assets' Projector/Multiply and Projector/Light over the stage (ortho and
// perspective, ignoreLayers, a disabled one), the unity_Projector and unity_ProjectorClip matrices
// they draw with, a new projector's defaults and one loaded from the scene.
public partial class FxProbe
{
    /// <summary>A cookie: a colored disc (u to red, v to green) on white (for Multiply) or black.</summary>
    Texture2D Cookie(bool multiply)
    {
        var t = Add(new Texture2D(64, 64, TextureFormat.RGBA32, false));
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float u = (x + 0.5f) / 64f, v = (y + 0.5f) / 64f;
                bool inside = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) < 0.4f;
                var outside = multiply ? Color.white : Color.black;
                t.SetPixel(x, y, inside ? new Color(u, 0.3f + 0.6f * v, 1f - u, 1f) : outside);
            }
        t.wrapMode = TextureWrapMode.Clamp;
        t.Apply();
        return t;
    }

    /// <summary>A falloff: alpha from 1 at u = 0 to 0 at u = 1.</summary>
    Texture2D Falloff()
    {
        var t = Add(new Texture2D(64, 4, TextureFormat.RGBA32, false));
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 64; x++)
                t.SetPixel(x, y, new Color(1f, 1f, 1f, 1f - x / 63f));
        t.wrapMode = TextureWrapMode.Clamp;
        t.Apply();
        return t;
    }

    Projector MakeProjector(Vector3 position, Quaternion rotation, Material material)
    {
        var go = Add(new GameObject("projector"));
        go.transform.SetPositionAndRotation(position, rotation);
        var p = go.AddComponent<Projector>();
        p.material = material;
        return p;
    }

    void ProjectorValues(string prefix, Projector p)
    {
        Value(prefix + ".nearClipPlane", F(p.nearClipPlane));
        Value(prefix + ".farClipPlane", F(p.farClipPlane));
        Value(prefix + ".fieldOfView", F(p.fieldOfView));
        Value(prefix + ".aspectRatio", F(p.aspectRatio));
        Value(prefix + ".orthographic", B(p.orthographic));
        Value(prefix + ".orthographicSize", F(p.orthographicSize));
        Value(prefix + ".ignoreLayers", I(p.ignoreLayers));
        Value(prefix + ".material", N(p.material));
        Value(prefix + ".enabled", B(p.enabled));
    }

    /// <summary>The matrices a projector draws a transformed quad with.</summary>
    void DumpProjector(string name, System.Action<Projector> setup)
    {
        var rt = Add(new RenderTexture(32, 1, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
        var go = Add(new GameObject("dump camera"));
        go.transform.position = new Vector3(0f, 0f, -10f);
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.targetTexture = rt;
        var quad = Primitive(PrimitiveType.Quad, new Vector3(0.3f, 0.2f, 2f), Unlit(Color.black, "Probe/Blank"));
        quad.transform.rotation = Quaternion.Euler(20f, 30f, 10f);
        quad.transform.localScale = new Vector3(1.5f, 1f, 1f);
        var projector = MakeProjector(new Vector3(-1f, 0.5f, -2f), Quaternion.LookRotation(quad.transform.position - new Vector3(-1f, 0.5f, -2f)),
            Add(new Material(Shader.Find("Probe/ProjectorDump"))));
        setup(projector);
        camera.Render();
        var t = Read(rt, TextureFormat.RGBAFloat);
        var a = new float[16];
        var b = new float[16];
        for (int i = 0; i < 16; i++)
        {
            a[i] = t.GetPixel(i, 0).r;
            b[i] = t.GetPixel(16 + i, 0).r;
        }
        DestroyImmediate(t);
        Value("projector." + name + ".unity_Projector", Floats(a));
        Value("projector." + name + ".unity_ProjectorClip", Floats(b));
        Clear();
    }

    void Projectors()
    {
        var fresh = Add(new GameObject("fresh")).AddComponent<Projector>();
        ProjectorValues("projector.defaults", fresh);
        Clear();
        foreach (var p in FindObjectsByType<Projector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (p.gameObject.scene.IsValid()) ProjectorValues("projector.scene", p);

        DumpProjector("perspective", p =>
        {
            p.fieldOfView = 40f;
            p.aspectRatio = 1.5f;
            p.nearClipPlane = 0.5f;
            p.farClipPlane = 8f;
        });
        DumpProjector("orthographic", p =>
        {
            p.orthographic = true;
            p.orthographicSize = 1.7f;
            p.aspectRatio = 0.8f;
            p.nearClipPlane = 1f;
            p.farClipPlane = 6f;
        });
        DumpProjector("scaled", p =>
        {
            p.transform.localScale = new Vector3(2f, 3f, 0.5f);
            p.fieldOfView = 30f;
        });

        var multiply = Add(new Material(Shader.Find("Projector/Multiply")));
        multiply.SetTexture("_ShadowTex", Cookie(true));
        multiply.SetTexture("_FalloffTex", Falloff());
        var light = Add(new Material(Shader.Find("Projector/Light")));
        light.SetTexture("_ShadowTex", Cookie(false));
        light.SetTexture("_FalloffTex", Falloff());
        light.color = new Color(1f, 0.8f, 0.5f);

        // Orthographic Multiply from above; the right sphere's layer is ignored.
        var stage = ColorStage();
        Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        stage[2].layer = 9;
        var camera = MakeCamera();
        var ortho = MakeProjector(new Vector3(0f, 3f, 0.3f), Quaternion.Euler(90f, 20f, 0f), multiply);
        ortho.orthographic = true;
        ortho.orthographicSize = 1.4f;
        ortho.aspectRatio = 1.3f;
        ortho.nearClipPlane = 0.5f;
        ortho.farClipPlane = 4f;
        ortho.ignoreLayers = 1 << 9;
        Capture(camera, "projector_multiply_ortho");
        ortho.enabled = false;
        Capture(camera, "projector_disabled");
        ortho.enabled = true;
        ortho.ignoreLayers = 0;
        ortho.material = null;
        Capture(camera, "projector_no_material");
        Clear();

        // Perspective Light from the side, with a transparent sphere in front.
        ColorStage();
        Directional(new Vector3(50f, -30f, 0f), Color.white, 0.6f);
        var transparent = Add(new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse")));
        transparent.color = new Color(1f, 1f, 1f, 0.4f);
        Primitive(PrimitiveType.Sphere, new Vector3(0.4f, 0.7f, -1.3f), transparent, 0.6f);
        camera = MakeCamera();
        var from = new Vector3(-2.5f, 2.5f, -1.5f);
        var perspective = MakeProjector(from, Quaternion.LookRotation(new Vector3(0f, 0.4f, 0.4f) - from), light);
        perspective.fieldOfView = 35f;
        perspective.aspectRatio = 1.6f;
        perspective.nearClipPlane = 1f;
        perspective.farClipPlane = 7f;
        Capture(camera, "projector_light_perspective");
        Clear();
    }
}
