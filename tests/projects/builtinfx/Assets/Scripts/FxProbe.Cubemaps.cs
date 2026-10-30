using UnityEngine;
using UnityEngine.Rendering;

// Camera.RenderToCubemap into a Cubemap and a cube RenderTexture, all faces and a face mask, from a
// rotated camera; per-camera Skybox components.
public partial class FxProbe
{
    /// <summary>Colored cubes on every side of (0, 1, 0) and a lit ground.</summary>
    void CubeStage()
    {
        Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        Primitive(PrimitiveType.Plane, Vector3.zero, Standard(new Color(0.6f, 0.6f, 0.6f)));
        Primitive(PrimitiveType.Cube, new Vector3(3f, 1f, 0f), Unlit(Color.red), 1.5f);
        Primitive(PrimitiveType.Cube, new Vector3(-3f, 1f, 0.5f), Unlit(Color.green), 1.5f);
        Primitive(PrimitiveType.Cube, new Vector3(0f, 1f, 3f), Unlit(Color.blue), 1.5f);
        Primitive(PrimitiveType.Sphere, new Vector3(0.5f, 1f, -3f), Unlit(Color.yellow), 1.5f);
        Primitive(PrimitiveType.Cube, new Vector3(0f, 4f, 0f), Unlit(Color.magenta), 1.5f);
    }

    Camera CubeCamera()
    {
        var camera = MakeCamera();
        camera.transform.SetPositionAndRotation(new Vector3(0f, 1f, 0f), Quaternion.Euler(20f, 40f, 10f));
        camera.nearClipPlane = 0.2f;
        camera.farClipPlane = 30f;
        camera.fieldOfView = 30f;
        return camera;
    }

    void SaveFaces(RenderTexture cube, string name)
    {
        var face = Add(new RenderTexture(cube.width, cube.height, 0, RenderTextureFormat.ARGB32));
        face.Create();
        for (int i = 0; i < 6; i++)
        {
            Graphics.CopyTexture(cube, i, 0, face, 0, 0);
            Save(face, name + "_" + i);
        }
    }

    RenderTexture ClearedCube(Color color)
    {
        var cube = Add(new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32) { dimension = TextureDimension.Cube });
        cube.Create();
        var active = RenderTexture.active;
        for (int i = 0; i < 6; i++)
        {
            Graphics.SetRenderTarget(cube, 0, (CubemapFace)i);
            GL.Clear(true, true, color);
        }
        RenderTexture.active = active;
        return cube;
    }

    void Cubemaps()
    {
        CubeStage();
        var camera = CubeCamera();
        var cubemap = Add(new Cubemap(64, TextureFormat.RGBA32, false));
        Value("cubemap.texture.result", B(camera.RenderToCubemap(cubemap)));
        for (int i = 0; i < 6; i++)
        {
            var face = Add(new Texture2D(64, 64, TextureFormat.RGBA32, false));
            face.SetPixels(cubemap.GetPixels((CubemapFace)i));
            face.Apply();
            Save(face, "cubemap_texture_" + i);
        }
        var masked = Add(new Cubemap(64, TextureFormat.RGBA32, false));
        Value("cubemap.texture_masked.result", B(camera.RenderToCubemap(masked, (1 << 0) | (1 << 5))));
        Value("cubemap.texture_masked.pixels", "[" + C(masked.GetPixel(CubemapFace.PositiveX, 32, 32)) + "," + C(masked.GetPixel(CubemapFace.PositiveY, 32, 32)) + "," + C(masked.GetPixel(CubemapFace.NegativeZ, 32, 32)) + "]");

        var rt = ClearedCube(new Color(0.5f, 0f, 0.5f, 1f));
        Value("cubemap.rt.result", B(camera.RenderToCubemap(rt)));
        SaveFaces(rt, "cubemap_rt");
        var rtMasked = ClearedCube(new Color(0.5f, 0f, 0.5f, 1f));
        Value("cubemap.rt_masked.result", B(camera.RenderToCubemap(rtMasked, (1 << 1) | (1 << 4))));
        SaveFaces(rtMasked, "cubemap_rt_masked");
        var flat = Add(new RenderTexture(64, 64, 24));
        Value("cubemap.rt_2d.result", B(camera.RenderToCubemap(flat)));
        Value("cubemap.camera_after", V3(camera.transform.eulerAngles) + "");
        Value("cubemap.camera_fov_after", F(camera.fieldOfView));
        Value("cubemap.camera_target_after", N(camera.targetTexture));
        Clear();
    }

    void Skyboxes()
    {
        var a = Unlit(new Color(0.3f, 0.5f, 1f), "Probe/Sky");
        a.name = "SkyA";
        var b = Unlit(new Color(1f, 0.6f, 0.3f), "Probe/Sky");
        b.name = "SkyB";
        RenderSettings.skybox = a;
        ColorStage();
        Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        var camera = MakeCamera();
        camera.clearFlags = CameraClearFlags.Skybox;
        Capture(camera, "sky_settings");
        var sky = camera.gameObject.AddComponent<Skybox>();
        Value("skybox.defaultMaterial", N(sky.material));
        Capture(camera, "sky_component_empty");
        sky.material = b;
        Capture(camera, "sky_component");
        sky.enabled = false;
        Capture(camera, "sky_component_disabled");
        sky.enabled = true;
        RenderSettings.skybox = null;
        Capture(camera, "sky_component_no_settings");
        camera.clearFlags = CameraClearFlags.SolidColor;
        Capture(camera, "sky_component_solid");
        var sceneCamera = SceneObject("SceneCamera");
        Value("skybox.scene", sceneCamera == null ? "null" : N(sceneCamera.GetComponent<Skybox>().material));
        Clear();
    }
}
