using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

public partial class LineProbe
{
    void ShadowControls()
    {
        if (Environment.GetEnvironmentVariable("LINE_SHADOW_PROBE") != "1") return;
        foreach (var renderer in FindObjectsOfType<Renderer>()) renderer.enabled = false;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        QualitySettings.shadows = ShadowQuality.HardOnly;
        QualitySettings.shadowCascades = 1;
        QualitySettings.shadowDistance = 30f;
        QualitySettings.pixelLightCount = 4;
        var camera = NewCamera("ShadowControls", true, new Vector3(0,0,-10), Vector3.zero);
        camera.enabled = false;
        camera.cullingMask = 1 << ImageLayer;
        var sunObject = new GameObject("ShadowControlsLight");
        var light = sunObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = LightShadows.Hard;
        light.shadowBias = 0;
        light.shadowNormalBias = 0;
        light.shadowCustomResolution = 64;
        light.cullingMask = 1 << ImageLayer;
        RenderSettings.sun = light;
        var material = new Material(Resources.Load<Shader>("LineShadowPosition"));
        var read = new Material(Resources.Load<Shader>("LineShadowRead"));
        var target = new RenderTexture(64,64,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        target.Create();
        var command = new CommandBuffer();
        command.SetGlobalTexture("_ProbeShadowMap", new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive), RenderTextureSubElement.Depth);
        command.Blit(Texture2D.blackTexture, target, read, 0);
        light.AddCommandBuffer(LightEvent.AfterShadowMap, command);
        foreach (bool trail in new[] { false, true })
        {
            var go = new GameObject("ShadowControlStrip") { layer = ImageLayer };
            var points = new[] { new Vector3(-0.6f,0,0), new Vector3(0.6f,0,0) };
            LineRenderer line = null;
            TrailRenderer tr = null;
            Renderer renderer;
            if (trail)
            {
                tr = go.AddComponent<TrailRenderer>();
                tr.emitting = true;
                tr.time = 100;
                tr.Clear();
                tr.AddPositions(points);
                tr.emitting = false;
                tr.widthCurve = AnimationCurve.Constant(0,1,1);
                tr.minVertexDistance = 0;
                tr.widthMultiplier = 0.3f;
                renderer = tr;
            }
            else
            {
                line = go.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.SetPositions(points);
                line.widthMultiplier = 0.3f;
                renderer = line;
            }
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            renderer.receiveShadows = true;
            foreach (float bias in new[] { 0f, 0.5f, 1f })
            {
                if (trail) tr.shadowBias = bias; else line.shadowBias = bias;
                string key = "shadow." + (trail ? "trail" : "line") + ".bias" + F(bias);
                Bake(key + ".bake", renderer, camera, false);
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                GL.Clear(false,true,new Color(-1,-1,-1,-1));
                RenderTexture.active = previous;
                Image(key + ".normal",camera);
                SaveShadowTarget(key + ".map",target);
            }
            if (trail) tr.alignment = LineAlignment.TransformZ; else line.alignment = LineAlignment.TransformZ;
            go.transform.rotation = Quaternion.Euler(0,25,15);
            if (trail) tr.widthCurve = AnimationCurve.Linear(0,0.5f,1,1.5f);
            else line.widthCurve = AnimationCurve.Linear(0,0.5f,1,1.5f);
            foreach (float bias in new[] { 0f, 0.5f })
            {
                if (trail) tr.shadowBias = bias; else line.shadowBias = bias;
                string key = "shadow." + (trail ? "trail" : "line") + ".transformZ.bias" + F(bias);
                Image(key + ".normal",camera);
                SaveShadowTarget(key + ".map",target);
            }
            if (trail) { tr.alignment = LineAlignment.View; tr.widthCurve = AnimationCurve.Constant(0,1,1); }
            else { line.alignment = LineAlignment.View; line.widthCurve = AnimationCurve.Constant(0,1,1); }
            camera.transform.position = new Vector3(6,2,-3);
            camera.transform.LookAt(Vector3.zero);
            sunObject.transform.rotation = Quaternion.Euler(0,55,0);
            foreach (float bias in new[] { 0f, 0.5f })
            {
                if (trail) tr.shadowBias = bias; else line.shadowBias = bias;
                string key = "shadow." + (trail ? "trail" : "line") + ".angledView.bias" + F(bias);
                Image(key + ".normal",camera);
                SaveShadowTarget(key + ".map",target);
            }
            var framePoints = new[] { new Vector3(-0.6f,0f,0f), new Vector3(0.05f,0.25f,0.15f), new Vector3(0.65f,-0.15f,0.3f) };
            if (trail)
            {
                tr.emitting = true;
                tr.Clear();
                tr.AddPositions(framePoints);
                tr.emitting = false;
                tr.numCornerVertices = tr.numCapVertices = 2;
            }
            else
            {
                line.positionCount = framePoints.Length;
                line.SetPositions(framePoints);
                line.numCornerVertices = line.numCapVertices = 2;
            }
            sunObject.transform.position = new Vector3(1.1f,0.8f,-0.7f);
            foreach (string control in new[] { "orthoNear03", "orthoNear2", "perspective", "scaledLight" })
            {
                camera.orthographic = control != "perspective";
                camera.nearClipPlane = control == "orthoNear2" ? 2f : 0.3f;
                sunObject.transform.localScale = control == "scaledLight" ? new Vector3(2,3,4) : Vector3.one;
                string key = "shadow." + (trail ? "trail" : "line") + ".lightFrame." + control;
                Bake(key + ".bake",renderer,camera,false);
                Image(key + ".normal",camera);
                SaveShadowTarget(key + ".map",target);
            }
            camera.orthographic = true;
            camera.nearClipPlane = 0.3f;
            camera.transform.position = new Vector3(0,0,-10);
            camera.transform.LookAt(Vector3.zero);
            sunObject.transform.rotation = Quaternion.identity;
            sunObject.transform.position = Vector3.zero;
            sunObject.transform.localScale = Vector3.one;
            DestroyImmediate(go);
        }
        light.RemoveAllCommandBuffers();
        command.Release();
        target.Release();
        DestroyImmediate(target);
        DestroyImmediate(material);
        DestroyImmediate(read);
        DestroyImmediate(camera.gameObject);
        DestroyImmediate(sunObject);
        RenderSettings.sun = null;
        LocalShadowControls();
        CascadeShadowControls();
    }

    void SaveShadowTarget(string key, RenderTexture target)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);
        image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);
        image.Apply();
        RenderTexture.active = previous;
        var pixels = image.GetPixels();
        int covered = 0;
        foreach (var pixel in pixels) if (pixel.r >= 0 && pixel.r < 0.9999f) covered++;
        Value(key + ".covered", covered.ToString());
        using (var writer = new BinaryWriter(File.Create(Path.Combine(directory,key + ".rgba"))))
            foreach (var pixel in pixels) { writer.Write(pixel.r);writer.Write(pixel.g);writer.Write(pixel.b);writer.Write(pixel.a); }
        Value(key + ".pixels",pixels.Length.ToString());
        DestroyImmediate(image);
    }
}
