using System;
using UnityEngine;
using UnityEngine.Rendering;

public partial class FxProbe
{
    /// <summary>Recorded after the deferred stencil part: where a deferred camera draws its point
    /// and spot lights. The custom deferred shading shader (Probe/Settings/Deferred) adds the same
    /// colour for every light it draws, so each image (<c>deferred_volume_&lt;case&gt;</c>) shows
    /// the pixels one light covers, over a floor, a wall, a sphere and a cube: a point light small
    /// on the screen and a large one, one around the camera, one crossing the far plane, one around
    /// the camera that crosses the far plane too, a spot light seen from outside, one whose cone
    /// holds the camera and one crossing the far plane.</summary>
    void DeferredVolumes()
    {
        var shader = Shader.Find("Probe/Settings/Deferred");
        var initial = GraphicsSettings.GetCustomShader(BuiltinShaderType.DeferredShading);
        GraphicsSettings.SetShaderMode(BuiltinShaderType.DeferredShading, BuiltinShaderMode.UseCustom);
        GraphicsSettings.SetCustomShader(BuiltinShaderType.DeferredShading, shader);
        try
        {
            VolumeCase("point_small", 20f, (camera) => Point(new Vector3(0.8f, 0.4f, 1.5f), Color.white, 0.45f, 1f));
            VolumeCase("point_large", 20f, (camera) => Point(new Vector3(-0.8f, 1.2f, -0.8f), Color.white, 3f, 1f));
            VolumeCase("point_inside", 20f, (camera) => Point(camera.transform.position + camera.transform.forward, Color.white, 2.5f, 1f));
            VolumeCase("point_far", 12f, (camera) => Point(new Vector3(0.5f, 1f, 5.5f), Color.white, 2.5f, 1f));
            VolumeCase("point_quad", 12f, (camera) => Point(camera.transform.position + 2f * camera.transform.forward, Color.white, 14f, 1f));
            VolumeCase("spot", 20f, (camera) => Spot(new Vector3(0f, 3f, 0.5f), new Vector3(0.3f, 0f, 0.8f), 50f, 5f));
            VolumeCase("spot_inside", 20f, (camera) => Spot(camera.transform.position, camera.transform.position + camera.transform.forward, 60f, 8f));
            VolumeCase("spot_far", 12f, (camera) => Spot(new Vector3(0f, 3f, 2f), new Vector3(0f, 0f, 6f), 40f, 8f));
        }
        finally
        {
            GraphicsSettings.SetShaderMode(BuiltinShaderType.DeferredShading, BuiltinShaderMode.UseBuiltin);
            GraphicsSettings.SetCustomShader(BuiltinShaderType.DeferredShading, initial);
        }
    }

    void VolumeCase(string name, float far, Func<Camera, Light> light)
    {
        try
        {
            var floor = Primitive(PrimitiveType.Plane, Vector3.zero, Standard(new Color(0.7f, 0.7f, 0.7f)));
            floor.transform.localScale = new Vector3(3f, 1f, 3f);
            var wall = Primitive(PrimitiveType.Quad, new Vector3(0f, 5f, 6f), Standard(new Color(0.6f, 0.6f, 0.7f)), 30f);
            Primitive(PrimitiveType.Sphere, new Vector3(-1f, 0.5f, 0f), Standard(new Color(1f, 0.4f, 0.3f)));
            Primitive(PrimitiveType.Cube, new Vector3(1f, 0.5f, 1f), Standard(new Color(0.3f, 0.4f, 1f)));
            var camera = MakeCamera();
            camera.renderingPath = RenderingPath.DeferredShading;
            camera.farClipPlane = far;
            light(camera);
            Value("deferredVolumes." + name + ".actualPath", S(camera.actualRenderingPath.ToString()));
            Capture(camera, "deferred_volume_" + name);
        }
        catch (Exception e)
        {
            Value("deferredVolumes." + name + ".exception", S(e.GetType().Name + ": " + e.Message));
        }
        Clear();
        Environment(new Color(0.1f, 0.1f, 0.1f));
    }

    Light Spot(Vector3 position, Vector3 target, float angle, float range)
    {
        var go = Add(new GameObject("spot"));
        go.transform.position = position;
        go.transform.LookAt(target);
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot;
        l.spotAngle = angle;
        l.range = range;
        return l;
    }

    /// <summary>The stencil a deferred camera that clears nothing finds and leaves: a Standard
    /// sphere drawn on the left by a render that clears to a solid colour (a new target's contents
    /// are undefined, RenderTexture.Create), then moved to the right and drawn again into the same
    /// target (24-bit depth) with the camera's clear flags, with a transparent quad over the view
    /// in that second render drawing white where stencil bit 7 (the G-buffer's mark) is set:
    /// <c>deferredDontClear.&lt;camera&gt;.&lt;point&gt;</c> at the sphere's first place, its
    /// second and the background, for an HDR and an LDR camera that clear nothing, and HDR and LDR
    /// cameras that clear to a solid colour (the controls); <c>ldrOther</c>: an LDR camera that
    /// clears nothing, with another LDR deferred camera of the same size rendering into its own
    /// target between the two renders. Then the command buffer draws of <see cref="DeferredEventDraws"/>.</summary>
    void DeferredDontClear()
    {
        DontClearCase("hdr", true, CameraClearFlags.Nothing);
        DontClearCase("ldr", false, CameraClearFlags.Nothing);
        DontClearCase("hdrSolid", true, CameraClearFlags.SolidColor);
        DontClearCase("ldrSolid", false, CameraClearFlags.SolidColor);
        DontClearCase("ldrOther", false, CameraClearFlags.Nothing, true);
        DeferredEventDraws("ldr", false);
        DeferredEventDraws("hdr", true);
    }

    /// <summary>What a command buffer draws into CameraTarget at the deferred camera events leaves
    /// in the image: a small unlit quad over the background at each of BeforeLighting,
    /// AfterLighting, BeforeFinalPass and AfterFinalPass (<c>deferredEvents.&lt;camera&gt;.&lt;event&gt;</c>:
    /// the colour read there), for an LDR and an HDR camera that clear to a solid colour.</summary>
    void DeferredEventDraws(string name, bool hdr)
    {
        string key = "deferredEvents." + name;
        try
        {
            Primitive(PrimitiveType.Sphere, new Vector3(0f, 0.5f, 0f), Standard(new Color(0.8f, 0.8f, 0.8f)));
            Directional(new Vector3(50f, -30f, 0f), Color.white, 0.8f);
            var camera = MakeCamera();
            camera.renderingPath = RenderingPath.DeferredShading;
            camera.allowHDR = hdr;
            camera.backgroundColor = Color.black;
            var quad = Add(Instantiate(Resources.GetBuiltinResource<Mesh>("Quad.fbx")));
            var events = new[] { CameraEvent.BeforeLighting, CameraEvent.AfterLighting, CameraEvent.BeforeFinalPass, CameraEvent.AfterFinalPass };
            var places = new[] { new Vector2Int(20, 20), new Vector2Int(Width - 20, 20), new Vector2Int(20, Height - 20), new Vector2Int(Width - 20, Height - 20) };
            var colors = new[] { new Color(1f, 0f, 0f, 1f), new Color(0f, 1f, 0f, 1f), new Color(0f, 0f, 1f, 1f), new Color(1f, 1f, 0f, 1f) };
            var buffers = new CommandBuffer[events.Length];
            for (int i = 0; i < events.Length; i++)
            {
                var material = Unlit(colors[i]);
                var world = camera.ScreenToWorldPoint(new Vector3(places[i].x + 0.5f, places[i].y + 0.5f, 2f));
                var matrix = Matrix4x4.TRS(world, camera.transform.rotation, Vector3.one * 0.15f);
                buffers[i] = new CommandBuffer { name = "deferredEvents." + events[i] };
                buffers[i].SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                buffers[i].DrawMesh(quad, matrix, material);
                camera.AddCommandBuffer(events[i], buffers[i]);
            }
            camera.Render();
            var t = Read(camera.targetTexture);
            for (int i = 0; i < events.Length; i++) Value(key + "." + events[i], C(t.GetPixel(places[i].x, places[i].y)));
            DestroyImmediate(t);
            for (int i = 0; i < events.Length; i++)
            {
                camera.RemoveCommandBuffer(events[i], buffers[i]);
                buffers[i].Release();
            }
        }
        catch (Exception e)
        {
            Value(key + ".exception", S(e.GetType().Name + ": " + e.Message));
        }
        Clear();
        Environment(new Color(0.1f, 0.1f, 0.1f));
    }

    /// <param name="other">Another LDR deferred camera of the same size renders a sphere of its
    /// own into its own target between the two renders (<c>.other</c>: that sphere's place in
    /// this camera's image).</param>
    void DontClearCase(string name, bool hdr, CameraClearFlags clear, bool other = false)
    {
        string key = "deferredDontClear." + name;
        try
        {
            var sphere = Primitive(PrimitiveType.Sphere, new Vector3(-1f, 0.5f, 0f), Standard(new Color(0.8f, 0.8f, 0.8f)));
            Directional(new Vector3(50f, -30f, 0f), Color.white, 0.8f);
            var camera = MakeCamera();
            camera.renderingPath = RenderingPath.DeferredShading;
            camera.allowHDR = hdr;
            camera.backgroundColor = Color.black;
            var test = Add(new Material(Shader.Find("Probe/StencilTest")));
            test.SetFloat("_Ref", 128f);
            test.SetFloat("_Mask", 128f);
            var quad = Primitive(PrimitiveType.Quad, camera.transform.position + camera.transform.forward, test, 4f);
            quad.transform.rotation = camera.transform.rotation;
            quad.SetActive(false);
            Vector2Int At(Vector3 p)
            {
                var s = camera.WorldToScreenPoint(p);
                return new Vector2Int((int)s.x, (int)s.y);
            }
            var first = At(sphere.transform.position);
            camera.Render();
            Vector2Int otherAt = default;
            if (other)
            {
                var otherCamera = MakeCamera();
                otherCamera.renderingPath = RenderingPath.DeferredShading;
                otherCamera.allowHDR = false;
                otherCamera.backgroundColor = Color.black;
                var otherSphere = Primitive(PrimitiveType.Sphere, new Vector3(0f, 1.5f, 0f), Standard(new Color(0.8f, 0.8f, 0.8f)));
                otherAt = At(otherSphere.transform.position);
                sphere.SetActive(false);
                otherCamera.Render();
                otherSphere.SetActive(false);
                sphere.SetActive(true);
            }
            camera.clearFlags = clear;
            sphere.transform.position = new Vector3(1f, 0.5f, 0f);
            var second = At(sphere.transform.position);
            quad.SetActive(true);
            camera.Render();
            var t = Read(camera.targetTexture);
            Value(key + ".first", B(t.GetPixel(first.x, first.y).g > 0.99f));
            Value(key + ".second", B(t.GetPixel(second.x, second.y).g > 0.99f));
            Value(key + ".background", B(t.GetPixel(2, Height - 3).g > 0.99f));
            if (other) Value(key + ".other", B(t.GetPixel(otherAt.x, otherAt.y).g > 0.99f));
            DestroyImmediate(t);
            Save(camera.targetTexture, "deferred_dontclear_" + name);
        }
        catch (Exception e)
        {
            Value(key + ".exception", S(e.GetType().Name + ": " + e.Message));
        }
        Clear();
        Environment(new Color(0.1f, 0.1f, 0.1f));
    }
}
