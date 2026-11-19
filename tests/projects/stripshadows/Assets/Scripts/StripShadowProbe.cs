using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class StripShadowProbe : MonoBehaviour
{
    public static Action<int> finished;
    string directory;
    readonly StringBuilder rows = new StringBuilder();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("STRIP_SHADOW_OUT") != null)
            new GameObject("StripShadowProbe").AddComponent<StripShadowProbe>();
    }
    void Value(string key, string value) => rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");
    IEnumerator Start()
    {
        directory = Environment.GetEnvironmentVariable("STRIP_SHADOW_OUT");
        Directory.CreateDirectory(directory);
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None)) r.enabled = false;
        foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.enabled = false;
        // Allow the selected pipeline to initialize before synchronous camera renders.
        yield return null;
        int code = 0;
        try { Capture(); Value("complete","true"); }
        catch (Exception e) { Value("fatal","\"" + e.GetType().Name + "\""); Debug.LogException(e); code = 1; }
        File.WriteAllText(Path.Combine(directory,"values.jsonl"),rows.ToString());
        if (finished != null) finished(code); else UnityEditor.EditorApplication.Exit(code);
    }
    void Capture()
    {
        var asset = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        var fields = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        asset.shadowCascadeCount = 1;
        asset.shadowDistance = 20;
        typeof(UniversalRenderPipelineAsset).GetField("m_MainLightShadowmapResolution",fields).SetValue(asset,128);
        typeof(UniversalRenderPipelineAsset).GetField("m_AdditionalLightsShadowmapResolution",fields).SetValue(asset,256);
        typeof(UniversalRenderPipelineAsset).GetField("m_AdditionalLightShadowsSupported",fields).SetValue(asset,true);
        var camera = new GameObject("StripCamera").AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = new Vector3(0,0,-6);
        camera.orthographic = true;
        camera.orthographicSize = 1.3f;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 30;
        camera.cullingMask = 1 << 9;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        var data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        data.renderShadows = true;
        var target = new RenderTexture(64,64,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        target.Create(); camera.targetTexture = target;
        var material = new Material(Resources.Load<Shader>("StripShadowPosition"));
        var read = new Material(Resources.Load<Shader>("StripShadowRead"));
        StripShadowFeature.Read = read;
        var light = new GameObject("StripLight").AddComponent<Light>();
        light.shadows = LightShadows.Hard;
        light.shadowBias = light.shadowNormalBias = 0;
        light.shadowCustomResolution = 64;
        light.range = 15;
        light.spotAngle = 100;
        light.cullingMask = camera.cullingMask;
        light.renderMode = LightRenderMode.ForcePixel;
        var receiver = GameObject.CreatePrimitive(PrimitiveType.Quad);
        receiver.layer = 9;
        receiver.transform.position = new Vector3(0,-0.8f,0.5f);
        receiver.transform.localScale = Vector3.one * 0.2f;
        var receiverMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        receiver.GetComponent<Renderer>().sharedMaterial = receiverMaterial;
        receiver.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        foreach (var type in new[] { LightType.Directional, LightType.Spot, LightType.Point })
        {
            light.type = type;
            RenderSettings.sun = type == LightType.Directional ? light : null;
            int size = type == LightType.Directional ? 128 : 256;
            var map = new RenderTexture(size,size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            map.Create();
            StripShadowFeature.Output=RTHandles.Alloc(map);
            StripShadowFeature.Main=type==LightType.Directional;
            foreach (string kind in new[] { "line", "trail", "mesh" })
            {
                bool trail=kind=="trail";
                Mesh controlMesh=null;
                var go = new GameObject("Strip") { layer = 9 };
                var points = new[] { new Vector3(-0.55f,-0.1f,0), new Vector3(0.05f,0.2f,0.1f), new Vector3(0.6f,-0.1f,0.25f) };
                Renderer renderer;
                LineRenderer line = null;
                TrailRenderer tr = null;
                if (kind=="mesh")
                {
                    controlMesh=new Mesh();
                    controlMesh.vertices=new[]{new Vector3(-0.6f,-0.15f,0),new Vector3(0.6f,-0.15f,0),new Vector3(0.6f,0.15f,0),new Vector3(-0.6f,0.15f,0)};
                    controlMesh.triangles=new[]{0,1,2,0,2,3};controlMesh.RecalculateBounds();
                    go.AddComponent<MeshFilter>().sharedMesh=controlMesh;renderer=go.AddComponent<MeshRenderer>();
                }
                else if (trail)
                {
                    go.transform.position=points[points.Length-1];
                    tr = go.AddComponent<TrailRenderer>();tr.time=100;tr.Clear();tr.emitting=true;tr.AddPositions(points);tr.emitting=false;
                    tr.widthMultiplier=0.3f;tr.widthCurve=AnimationCurve.Linear(0,0.5f,1,1.5f);tr.numCapVertices=tr.numCornerVertices=2;renderer=tr;
                }
                else
                {
                    line=go.AddComponent<LineRenderer>();line.positionCount=points.Length;line.SetPositions(points);
                    line.widthMultiplier=0.3f;line.widthCurve=AnimationCurve.Linear(0,0.5f,1,1.5f);line.numCapVertices=line.numCornerVertices=2;renderer=line;
                }
                renderer.sharedMaterial=material;
                renderer.shadowCastingMode=ShadowCastingMode.TwoSided;
                var controls=type==LightType.Directional && Environment.GetEnvironmentVariable("STRIP_SHADOW_CASCADES")=="1"
                    ?new[]{"view0","viewBias","turnedLight","transformZ","cascade2","cascade2Wide","cascade3","cascade4","cascade4Wide","cascade4Ortho","cascade3Close","cascade4WideClose"}
                    :new[]{"view0","viewBias","turnedLight","transformZ"};
                foreach (string control in controls)
                {
                    bool cascaded=control.StartsWith("cascade");
                    asset.shadowCascadeCount=cascaded?int.Parse(control.Substring(7,1)):1;
                    asset.cascade2Split=control=="cascade2Wide"?0.6f:0.25f;
                    asset.cascade3Split=new Vector2(0.2f,0.6f);
                    asset.cascade4Split=control.StartsWith("cascade4Wide")?new Vector3(0.2f,0.5f,0.8f):new Vector3(0.067f,0.2f,0.467f);
                    // The close-fit controls repeat a stable-fit control with close-fit projection.
                    QualitySettings.shadowProjection=control.EndsWith("Close")?ShadowProjection.CloseFit:ShadowProjection.StableFit;
                    float bias=control=="view0"?0:0.5f;
                    var alignment=control=="transformZ"?LineAlignment.TransformZ:LineAlignment.View;
                    if(trail){tr.shadowBias=bias;tr.alignment=alignment;}else if(line!=null){line.shadowBias=bias;line.alignment=alignment;}
                    go.transform.rotation=Quaternion.Euler(0,25,15);
                    light.transform.SetPositionAndRotation(new Vector3(0.2f,0.4f,-3),control=="turnedLight"?Quaternion.Euler(12,20,9):Quaternion.identity);
                    light.transform.localScale=control=="turnedLight"?new Vector3(2,3,4):Vector3.one;
                    camera.orthographic=cascaded?control=="cascade4Ortho":control!="turnedLight";
                    string key=type+"."+kind+"."+control;
                    camera.Render();camera.Render();
                    Save(key+".normal",target,false);
                    Save(key+".map",map,true);
                    var bake=new Mesh();
                    if(trail)tr.BakeMesh(bake,camera,false);else if(line!=null)line.BakeMesh(bake,camera,false);else bake.vertices=controlMesh.vertices;
                    var vertices=new StringBuilder("[");
                    foreach(var v in bake.vertices) { if(vertices.Length>1)vertices.Append(',');vertices.Append(v.x.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(v.y.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(v.z.ToString("R",CultureInfo.InvariantCulture)); }
                    Value(key+".bake",vertices.Append(']').ToString());DestroyImmediate(bake);
                }
                DestroyImmediate(go);if(controlMesh!=null)DestroyImmediate(controlMesh);
            }
            StripShadowFeature.Output.Release();StripShadowFeature.Output=null;
            RenderTexture.active=null;map.Release();DestroyImmediate(map);
        }
        target.Release();DestroyImmediate(target);DestroyImmediate(camera.gameObject);DestroyImmediate(light.gameObject);
        DestroyImmediate(receiver);DestroyImmediate(receiverMaterial);DestroyImmediate(material);DestroyImmediate(read);
    }
    void Save(string key, RenderTexture target, bool depth)
    {
        var previous=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);
        image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();RenderTexture.active=previous;
        var pixels=image.GetPixels();int covered=0;
        using(var writer=new BinaryWriter(File.Create(Path.Combine(directory,key+".rgba"))))
            foreach(var pixel in pixels){writer.Write(pixel.r);writer.Write(pixel.g);writer.Write(pixel.b);writer.Write(pixel.a);if(pixel.r>0.01f&&pixel.r<0.99f)covered++;}
        if(depth)Value(key+".covered",covered.ToString());Value(key+".pixels",pixels.Length.ToString());DestroyImmediate(image);
    }
}
