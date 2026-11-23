using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public sealed class MeasureSpriteDrawLifetime : MonoBehaviour
{
    [Serializable] sealed class Row { public string k, v; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        string output = Environment.GetEnvironmentVariable("SPRITE_MEMORY_OUT");
        if (!string.IsNullOrEmpty(output))
            new GameObject("Sprite allocation probe").AddComponent<MeasureSpriteDrawLifetime>().StartCoroutine(Capture(output));
    }

    static IEnumerator Capture(string output)
    {
        // The graphics device is available after the first frame.
        yield return null;

        var rows = new List<string>();
        void Put(string key, string value) => rows.Add(JsonUtility.ToJson(new Row { k = key, v = value }));
        void Read(string stage, Sprite sprite) => Put(stage, Profiler.GetRuntimeMemorySizeLong(sprite).ToString());
        Put("unity", Application.unityVersion);
        var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var pixels = new Color32[64 * 64];
        Array.Fill(pixels, new Color32(255, 128, 0, 255));
        texture.SetPixels32(pixels);
        texture.Apply();
        Sprite Make() => Sprite.Create(texture, new Rect(0, 0, 64, 64), Vector2.one * 0.5f, 32f, 0,
            SpriteMeshType.FullRect, new Vector4(4f, 4f, 4f, 4f), false);
        var defaultSprite = Sprite.Create(texture, new Rect(0, 0, 64, 64), Vector2.one * 0.5f);
        Read("default.created", defaultSprite);
        var firstGeneration = Object.Instantiate(defaultSprite);
        Read("default.sourceCloned", defaultSprite);
        var secondGeneration = Object.Instantiate(firstGeneration);
        Read("default.copyCloned", firstGeneration);
        Read("default.independentSecondCopy", secondGeneration);
        Object.DestroyImmediate(secondGeneration);
        Object.DestroyImmediate(firstGeneration);
        Object.DestroyImmediate(defaultSprite);
        var camera = new GameObject("Sprite draw allocation camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 3f;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.cullingMask = 1 << 12;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        var target = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;

        var sprite = Make();
        var clone = Object.Instantiate(sprite);
        var renderer = new GameObject("Existing shared sprite").AddComponent<SpriteRenderer>();
        renderer.gameObject.layer = 12;
        renderer.sprite = sprite;
        Read("share.sourceAfterClone", sprite);
        Read("share.cloneBeforeDraw", clone);
        renderer.transform.position = Vector3.right * 1000f;
        camera.Render();
        Read("share.sourceCulled", sprite);
        renderer.transform.position = Vector3.zero;
        renderer.enabled = false;
        camera.Render();
        Read("share.sourceDisabled", sprite);
        renderer.enabled = true;
        camera.cullingMask = 0;
        camera.Render();
        Read("share.sourceExcludedLayer", sprite);
        camera.cullingMask = 1 << 12;
        camera.Render();
        Read("share.sourceDrawn", sprite);
        Read("share.cloneAfterSourceDraw", clone);
        sprite.OverrideGeometry(new[] { new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64) }, new ushort[] { 0, 1, 2 });
        Read("share.sourceGeometryChanged", sprite);
        camera.Render();
        Read("share.sourceDrawnAgain", sprite);
        renderer.sprite = clone;
        camera.Render();
        Read("share.cloneIndependentlyDrawn", clone);
        Object.DestroyImmediate(renderer.gameObject);
        Object.DestroyImmediate(clone);
        Object.DestroyImmediate(sprite);

        foreach (var mode in new[] { SpriteDrawMode.Simple, SpriteDrawMode.Sliced, SpriteDrawMode.Tiled })
        {
            var instance = Make();
            var draw = new GameObject("Sprite draw mode " + mode).AddComponent<SpriteRenderer>();
            draw.gameObject.layer = 12;
            draw.sprite = instance;
            draw.drawMode = mode;
            draw.size = Vector2.one * 2f;
            Read("mode." + mode + ".assigned", instance);
            camera.Render();
            Read("mode." + mode + ".drawn", instance);
            var copy = Object.Instantiate(instance);
            Read("mode." + mode + ".sourceAfterClone", instance);
            Read("mode." + mode + ".clone", copy);
            Object.DestroyImmediate(draw.gameObject);
            Object.DestroyImmediate(copy);
            Object.DestroyImmediate(instance);
        }

        bool HasColor()
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            readback.ReadPixels(new Rect(16, 16, 1, 1), 0, 0);
            bool lit = readback.GetPixel(0, 0).r > 0.1f;
            Object.DestroyImmediate(readback);
            RenderTexture.active = previous;
            return lit;
        }

        var maskSprite = Make();
        var mask = new GameObject("Allocation mask").AddComponent<SpriteMask>();
        mask.gameObject.layer = 12;
        mask.sprite = maskSprite;
        Read("mask.assigned", maskSprite);
        mask.transform.position = Vector3.right * 1000f;
        camera.Render();
        Read("mask.offscreen", maskSprite);
        mask.transform.position = Vector3.zero;
        mask.enabled = false;
        camera.Render();
        Read("mask.disabled", maskSprite);
        mask.enabled = true;
        mask.forceRenderingOff = true;
        camera.Render();
        Read("mask.forceRenderingOff", maskSprite);
        mask.forceRenderingOff = false;
        camera.cullingMask = 0;
        camera.Render();
        Read("mask.excludedLayer", maskSprite);
        camera.cullingMask = 1 << 12;
        camera.Render();
        Read("mask.renderedWithoutReceiver", maskSprite);
        Put("mask.noReceiverColor", HasColor().ToString());
        var receiverSprite = Make();
        var receiver = new GameObject("Allocation mask receiver").AddComponent<SpriteRenderer>();
        receiver.gameObject.layer = 12;
        receiver.sprite = receiverSprite;
        receiver.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        camera.Render();
        Put("mask.receiverColor", HasColor().ToString());
        Read("mask.renderedWithReceiver", maskSprite);
        Read("mask.receiverDrawn", receiverSprite);
        Object.DestroyImmediate(receiver.gameObject);
        Object.DestroyImmediate(mask.gameObject);
        Object.DestroyImmediate(maskSprite);
        Object.DestroyImmediate(receiverSprite);

        var controlledSprite = Make();
        var controlled = new GameObject("Sprite culling allocation controls").AddComponent<SpriteRenderer>();
        controlled.gameObject.layer = 12;
        controlled.sprite = controlledSprite;
        var callbacks = controlled.gameObject.AddComponent<SpriteMemoryCallbacks>();
        callbacks.Measured = controlledSprite;
        controlled.forceRenderingOff = true;
        camera.Render();
        Read("renderer.forceRenderingOff", controlledSprite);
        controlled.forceRenderingOff = false;
        var material = controlled.sharedMaterial;
        controlled.sharedMaterials = Array.Empty<Material>();
        camera.Render();
        Read("renderer.noMaterials", controlledSprite);
        controlled.sharedMaterial = material;
        camera.Render();
        Read("renderer.visible", controlledSprite);
        Put("renderer.becameVisibleCallback", callbacks.BecameVisible.ToString());
        Put("renderer.willRenderCallback", callbacks.WillRender.ToString());
        Object.DestroyImmediate(controlled.gameObject);
        Object.DestroyImmediate(controlledSprite);
        SpriteMemoryGeometry.Capture(Make, camera, 12, Read);

        var maskSourceSprite = Make();
        var unusedMaskSprite = Make();
        var maskSource = new GameObject("Mask with a source renderer").AddComponent<SpriteRenderer>();
        maskSource.gameObject.layer = 12;
        maskSource.sprite = maskSourceSprite;
        maskSource.enabled = false;
        var derivedMask = maskSource.gameObject.AddComponent<SpriteMask>();
        derivedMask.maskSource = SpriteMask.MaskSource.SupportedRenderers;
        derivedMask.sprite = unusedMaskSprite;
        Read("extra.maskSourceBeforeRender", maskSourceSprite);
        camera.Render();
        Read("extra.maskSourceAfterRender", maskSourceSprite);
        Read("extra.unusedMaskSprite", unusedMaskSprite);
        Object.DestroyImmediate(maskSource.gameObject);
        Object.DestroyImmediate(maskSourceSprite);
        Object.DestroyImmediate(unusedMaskSprite);

        var sourceSprite = Make();
        var particle = new GameObject("Sprite emittance source").AddComponent<ParticleSystem>();
        particle.gameObject.layer = 12;
        particle.Stop();
        var shape = particle.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sprite;
        shape.sprite = sourceSprite;
        Read("extra.particleSourceAssigned", sourceSprite);
        particle.Emit(2);
        Read("extra.particleSourceAfterEmit", sourceSprite);
        Put("extra.particleCount", particle.particleCount.ToString());
        camera.Render();
        Read("extra.particleSourceAfterRender", sourceSprite);
        Object.DestroyImmediate(particle.gameObject);
        Object.DestroyImmediate(sourceSprite);

        var pipeline = ScriptableObject.CreateInstance<SpriteMemoryPipeline>();
        var previousPipeline = GraphicsSettings.defaultRenderPipeline;
        var previousQuality = QualitySettings.renderPipeline;
        var pipelineSprite = Make();
        var pipelineReceiver = new GameObject("SRP sprite").AddComponent<SpriteRenderer>();
        pipelineReceiver.gameObject.layer = 12;
        pipelineReceiver.sprite = pipelineSprite;
        try
        {
            QualitySettings.renderPipeline = null;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            void RunRequest(SpriteMemoryPipeline.Request request)
            {
                for (int attempt = 0; attempt < 4 && !request.Completed; attempt++)
                    if (RenderPipeline.SupportsRenderRequest(camera, request))
                        RenderPipeline.SubmitRenderRequest(camera, request);
            }
            void ReadCull(string stage, SpriteMemoryPipeline.Request request)
            {
                Put(stage + ".beforeCull", request.BeforeCull.ToString());
                Put(stage + ".afterCull", request.AfterCull.ToString());
                Put(stage + ".afterSetup", request.AfterSetup.ToString());
                Put(stage + ".afterClear", request.AfterClear.ToString());
            }
            var skipped = new SpriteMemoryPipeline.Request { Measured = pipelineSprite };
            RunRequest(skipped);
            ReadCull("srp.simple", skipped);
            Put("srp.cullOnlyCompleted", skipped.Completed.ToString());
            Put("srp.cullOnlyColor", HasColor().ToString());
            Read("srp.cullOnly", pipelineSprite);
            var drawn = new SpriteMemoryPipeline.Request { Draw = true, Measured = pipelineSprite };
            RunRequest(drawn);
            Put("srp.drawCompleted", drawn.Completed.ToString());
            Put("srp.drawColor", HasColor().ToString());
            Read("srp.afterDraw", pipelineSprite);

            pipelineReceiver.enabled = false;
            var pipelineMaskSprite = Make();
            var pipelineMask = new GameObject("SRP sprite mask").AddComponent<SpriteMask>();
            pipelineMask.gameObject.layer = 12;
            pipelineMask.sprite = pipelineMaskSprite;
            var maskCull = new SpriteMemoryPipeline.Request { Measured = pipelineMaskSprite };
            RunRequest(maskCull);
            ReadCull("srp.mask", maskCull);
            Put("srp.mask.completed", maskCull.Completed.ToString());
            Put("srp.mask.color", HasColor().ToString());
            Read("srp.mask.afterSubmit", pipelineMaskSprite);
            Object.DestroyImmediate(pipelineMask.gameObject);
            Object.DestroyImmediate(pipelineMaskSprite);
        }
        finally
        {
            QualitySettings.renderPipeline = previousQuality;
            GraphicsSettings.defaultRenderPipeline = previousPipeline;
        }
        Object.DestroyImmediate(pipeline);
        Object.DestroyImmediate(pipelineReceiver.gameObject);
        Object.DestroyImmediate(pipelineSprite);
        Object.DestroyImmediate(camera.gameObject);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(texture);
        rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
