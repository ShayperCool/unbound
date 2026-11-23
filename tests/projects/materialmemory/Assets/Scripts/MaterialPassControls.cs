using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

static class MaterialPassControls
{
    internal static IEnumerator Run(Action<string, object> put)
    {
        var shader = Shader.Find("Hidden/Storage/ExplicitPass");
        var source = new Material(shader);
        source.SetColor("_Color", Color.red);
        source.SetFloat("_Value", .75f);
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(-1, -1), new Vector3(1, -1), new Vector3(1, 1), new Vector3(-1, 1) },
            triangles = new[] { 0, 1, 2, 0, 2, 3 }
        };
        var target = new RenderTexture(16, 16, 24);
        target.Create();
        var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        var owner = new GameObject("Pass control source") { layer = 29 };
        owner.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = owner.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = source;
        var camera = new GameObject("Pass control camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -3);
        camera.orthographic = true;
        camera.cullingMask = 1 << owner.layer;
        camera.targetTexture = target;
        camera.enabled = false;
        var savedQuality = QualitySettings.renderPipeline;
        var savedDefault = GraphicsSettings.defaultRenderPipeline;
        QualitySettings.renderPipeline = null;
        GraphicsSettings.defaultRenderPipeline = null;

        void Size(string key, Material material) => put(key, Profiler.GetRuntimeMemorySizeLong(material));
        void Pixel(string key)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(8, 8, 1, 1), 0, 0);
            RenderTexture.active = previous;
            Color32 color = readback.GetPixel(0, 0);
            put(key, color.r + "/" + color.g + "/" + color.b);
        }

        try
        {
            foreach (bool drawRenderer in new[] { false, true })
            foreach (int pass in new[] { -1, 0, 999 })
            {
                var instance = Object.Instantiate(source);
                string key = "commandPass." + (drawRenderer ? "renderer" : "mesh") + "." + pass;
                Size(key + ".cold", instance);
                using var commands = new CommandBuffer();
                commands.SetRenderTarget(target);
                commands.ClearRenderTarget(true, true, Color.black);
                if (drawRenderer) commands.DrawRenderer(renderer, instance, 0, pass);
                else commands.DrawMesh(mesh, Matrix4x4.identity, instance, 0, pass);
                Size(key + ".recorded", instance);
                Graphics.ExecuteCommandBuffer(commands);
                Size(key + ".executed", instance);
                Pixel(key + ".pixel");
                Object.Destroy(instance);
            }

            foreach (string tag in new[] { "", "PassControlPipeline" })
            {
                var pipeline = ScriptableObject.CreateInstance<MaterialPassPipeline>();
                pipeline.PipelineTag = tag;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                camera.enabled = true;
                yield return null;
                yield return null;
                camera.enabled = false;

                string key = "pipelinePass." + (tag.Length == 0 ? "emptyTag" : "namedTag");
                put(key + ".active", RenderPipelineManager.currentPipeline != null);
                put(key + ".tag", Shader.globalRenderPipeline);
                foreach (int pass in new[] { 0, 999 })
                {
                    var direct = Object.Instantiate(source);
                    string directKey = key + ".direct." + pass;
                    Size(directKey + ".cold", direct);
                    _ = direct.SetPass(pass);
                    Size(directKey + ".after", direct);
                    Object.Destroy(direct);

                    RecordDraw(key + ".draw." + pass, pass, filterOut: false);
                }
                RecordDraw(key + ".filtered", 0, filterOut: true);

                void RecordDraw(string drawKey, int pass, bool filterOut)
                {
                    var submitted = Object.Instantiate(source);
                    Size(drawKey + ".cold", submitted);
                    var request = new MaterialPassPipeline.Request { Material = submitted, Pass = pass, FilterOut = filterOut };
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    put(drawKey + ".complete", request.Completed);
                    put(drawKey + ".culled", request.AfterCull);
                    put(drawKey + ".drawn", request.AfterDraw);
                    put(drawKey + ".submitted", request.AfterSubmit);
                    Size(drawKey + ".after", submitted);
                    Pixel(drawKey + ".pixel");
                    Object.Destroy(submitted);
                }
                GraphicsSettings.defaultRenderPipeline = null;
                yield return null;
                Object.Destroy(pipeline);
            }
        }
        finally
        {
            QualitySettings.renderPipeline = savedQuality;
            GraphicsSettings.defaultRenderPipeline = savedDefault;
            Object.Destroy(camera.gameObject);
            Object.Destroy(owner);
            Object.Destroy(mesh);
            Object.Destroy(source);
            Object.Destroy(target);
            Object.Destroy(readback);
        }
    }
}
