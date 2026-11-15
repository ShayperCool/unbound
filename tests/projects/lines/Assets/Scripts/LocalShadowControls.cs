using UnityEngine;
using UnityEngine.Rendering;

public partial class LineProbe
{
    void LocalShadowControls()
    {
        var camera = NewCamera("LocalShadowControls", true, new Vector3(0,0,-6), Vector3.zero);
        camera.enabled = false;
        camera.cullingMask = 1 << ImageLayer;
        var light = new GameObject("LocalShadowLight").AddComponent<Light>();
        light.shadows = LightShadows.Hard;
        light.shadowBias = light.shadowNormalBias = 0;
        light.shadowCustomResolution = 64;
        light.range = 15;
        light.spotAngle = 100;
        light.cullingMask = camera.cullingMask;
        light.renderMode = LightRenderMode.ForcePixel;
        var material = new Material(Resources.Load<Shader>("LocalShadowPosition"));
        var read = new Material(Resources.Load<Shader>("LineShadowRead"));
        var cubeRead = new Material(Resources.Load<Shader>("LineShadowCubeRead"));
        // A visible lit receiver makes local-light shadows relevant to the built-in pipeline.
        var receiver = GameObject.CreatePrimitive(PrimitiveType.Quad);
        receiver.layer = ImageLayer;
        receiver.transform.position = new Vector3(0,-0.8f,0.5f);
        receiver.transform.localScale = new Vector3(0.2f,0.2f,0.2f);
        var receiverMaterial = new Material(Shader.Find("Standard"));
        receiver.GetComponent<Renderer>().sharedMaterial = receiverMaterial;
        receiver.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        foreach (var type in new[] { LightType.Spot, LightType.Point })
        {
            light.type = type;
            var targets = new RenderTexture[type == LightType.Point ? 6 : 1];
            var command = new CommandBuffer();
            command.SetGlobalTexture(type == LightType.Point ? "_ProbeShadowCube" : "_ProbeShadowMap",
                new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive), RenderTextureSubElement.Depth);
            for (int face = 0; face < targets.Length; face++)
            {
                targets[face] = new RenderTexture(64,64,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
                targets[face].Create();
                command.SetGlobalFloat("_ProbeCubeFace", face);
                command.Blit(Texture2D.blackTexture,targets[face],type == LightType.Point ? cubeRead : read,0);
            }
            light.AddCommandBuffer(LightEvent.AfterShadowMap,command);
            foreach (bool trail in new[] { false, true })
            {
                var go = new GameObject("LocalShadowStrip") { layer = ImageLayer };
                var points = new[] { new Vector3(-0.55f,-0.1f,0),new Vector3(0.05f,0.2f,0.1f),new Vector3(0.6f,-0.1f,0.25f) };
                LineRenderer line = null;
                TrailRenderer tr = null;
                Renderer renderer;
                if (trail)
                {
                    go.transform.position = points[points.Length-1];
                    tr = go.AddComponent<TrailRenderer>();
                    tr.time = 100;
                    tr.emitting = true;
                    tr.Clear();
                    tr.AddPositions(points);
                    tr.emitting = false;
                    tr.minVertexDistance = 0.1f;
                    tr.widthMultiplier = 0.3f;
                    tr.widthCurve = AnimationCurve.Linear(0,0.5f,1,1.5f);
                    tr.numCornerVertices = tr.numCapVertices = 2;
                    renderer = tr;
                }
                else
                {
                    line = go.AddComponent<LineRenderer>();
                    line.positionCount = points.Length;
                    line.SetPositions(points);
                    line.widthMultiplier = 0.3f;
                    line.widthCurve = AnimationCurve.Linear(0,0.5f,1,1.5f);
                    line.numCornerVertices = line.numCapVertices = 2;
                    renderer = line;
                }
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                foreach (string control in new[] { "view0", "viewBias", "turnedLight", "transformZ" })
                {
                    float bias = control == "view0" ? 0 : 0.5f;
                    var alignment = control == "transformZ" ? LineAlignment.TransformZ : LineAlignment.View;
                    if (trail) { tr.shadowBias = bias; tr.alignment = alignment; }
                    else { line.shadowBias = bias; line.alignment = alignment; }
                    go.transform.rotation = Quaternion.Euler(0,25,15);
                    light.transform.SetPositionAndRotation(new Vector3(0.2f,0.4f,-3),Quaternion.identity);
                    if (control == "turnedLight")
                    {
                        light.transform.rotation = Quaternion.Euler(12,20,9);
                        light.transform.localScale = new Vector3(2,3,4);
                        camera.orthographic = false;
                    }
                    else { light.transform.localScale = Vector3.one; camera.orthographic = true; }
                    string key = "shadowLocal." + type + "." + (trail ? "trail" : "line") + "." + control;
                    Bake(key + ".bake",renderer,camera,false);
                    Image(key + ".normal",camera);
                    for (int face = 0; face < targets.Length; face++) SaveShadowTarget(key + ".face" + face,targets[face]);
                }
                DestroyImmediate(go);
            }
            light.RemoveAllCommandBuffers();
            command.Release();
            foreach (var target in targets) { target.Release(); DestroyImmediate(target); }
        }
        DestroyImmediate(receiver);
        DestroyImmediate(receiverMaterial);
        DestroyImmediate(camera.gameObject);
        DestroyImmediate(light.gameObject);
        DestroyImmediate(material);
        DestroyImmediate(read);
        DestroyImmediate(cubeRead);
    }
}
