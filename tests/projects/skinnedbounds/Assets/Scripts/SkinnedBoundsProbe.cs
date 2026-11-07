using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class SkinnedBoundsProbe : MonoBehaviour
{
    public static Action<int> finished;
    sealed class Case
    {
        public string key;
        public SkinnedMeshRenderer skin;
        public Transform root, marker;
        public Animator animator;
        public RenderTexture target;
        public Texture2D pixels;
        public bool offscreen, explicitBounds, animatedRoot;
        public Camera camera;
    }
    readonly List<Case> cases = new List<Case>();
    readonly StringBuilder records = new StringBuilder();
    Material material;
    string directory;
    int frame;
    bool failed;
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string B(bool v) => v ? "true" : "false";
    static string Box(Bounds b) => "{\"c\":" + V(b.center) + ",\"s\":" + V(b.size) + "}";
    void Put(string k, string v) => records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(v).Append("}\n");
    void Bounds(string key, SkinnedMeshRenderer skin) => Put(key, "{\"local\":" + Box(skin.localBounds) + ",\"world\":" + Box(skin.bounds) + "}");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("SKINNEDBOUNDS_PROBE_OUT") == null) return;
        Time.captureDeltaTime = 0.02f;
        new GameObject("SkinnedBoundsProbe").AddComponent<SkinnedBoundsProbe>();
    }

    SkinnedMeshRenderer Skin(string name, Transform parent, Transform bone)
    {
        var mesh = new Mesh { name = "AuthoredSkin" };
        mesh.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(-.5f,.5f,0), new Vector3(.5f,.5f,0) };
        mesh.triangles = new[] {0,2,1,1,2,3};
        mesh.boneWeights = new[] { new BoneWeight { weight0=1 }, new BoneWeight { weight0=1 }, new BoneWeight { weight0=1 }, new BoneWeight { weight0=1 } };
        mesh.bindposes = new[] { Matrix4x4.identity };
        mesh.RecalculateBounds();
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var skin = go.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = mesh;
        skin.bones = new[] { bone };
        skin.sharedMaterial = material;
        skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        skin.rootBone = bone;
        return skin;
    }

    void StaticCases()
    {
        var root = new GameObject("StaticRoot").transform;
        var skin = Skin("StaticSkin", null, root);
        skin.enabled = false;
        skin.transform.position = new Vector3(20,3,2);
        Bounds("static/default", skin);
        skin.localBounds = new Bounds(new Vector3(1,2,3), new Vector3(2,4,6));
        Bounds("static/explicit", skin);
        root.position = new Vector3(4,5,6);
        root.rotation = Quaternion.Euler(0,0,90);
        root.localScale = new Vector3(2,3,4);
        Bounds("static/root-trs", skin);
        skin.transform.rotation = Quaternion.Euler(20,30,40);
        skin.transform.localScale = new Vector3(5,6,7);
        Bounds("static/renderer-trs", skin);
        skin.rootBone = skin.transform;
        Bounds("static/self", skin);
        skin.rootBone = null;
        Bounds("static/null", skin);
        skin.rootBone = root;
        Bounds("static/reassigned", skin);
        skin.bounds = new Bounds(new Vector3(9,8,7),new Vector3(6,5,4));
        root.position += Vector3.one;
        Bounds("static/world-override", skin);
        skin.ResetBounds();
        Bounds("static/reset-world", skin);
        skin.ResetLocalBounds();
        Bounds("static/reset-local", skin);
        skin.sharedMesh.bounds = new Bounds(new Vector3(2,3,4),new Vector3(4,6,8));
        Bounds("static/mesh-bounds-change", skin);
        skin.sharedMesh = Instantiate(skin.sharedMesh);
        Bounds("static/mesh-reassigned", skin);
        skin.localBounds = new Bounds(Vector3.zero, Vector3.zero);
        Bounds("static/zero", skin);
        var fresh = Skin("Fresh", null, root);
        fresh.enabled = false;
        fresh.sharedMesh.bounds = new Bounds(new Vector3(2,3,4),new Vector3(4,6,8));
        Bounds("static/default-mesh-mutated", fresh);
        fresh.sharedMesh = Instantiate(fresh.sharedMesh);
        Bounds("static/default-mesh-replaced", fresh);
        fresh.rootBone = null;
        Bounds("static/default-no-root", fresh);
        fresh.sharedMesh.bindposes = new[] { Matrix4x4.TRS(new Vector3(2,1,0),Quaternion.Euler(0,0,30),new Vector3(1,2,1)) };
        fresh.updateWhenOffscreen = true;
        Bounds("static/dynamic-bindpose", fresh);
        fresh.rootBone = root;
        Bounds("static/dynamic-root", fresh);
        var preset = Instantiate(fresh.sharedMesh);
        preset.bounds = new Bounds(new Vector3(30,40,50),new Vector3(60,70,80));
        var presetSkin = new GameObject("PresetBounds").AddComponent<SkinnedMeshRenderer>();
        presetSkin.enabled=false;
        presetSkin.bones=new[] {root};
        presetSkin.rootBone=root;
        presetSkin.sharedMesh=preset;
        Bounds("static/preset-mesh-bounds",presetSkin);
        Destroy(presetSkin.gameObject);
        Destroy(fresh.gameObject);
        Destroy(skin.gameObject);
        Destroy(root.gameObject);
    }

    void Build()
    {
        StaticCases();
        var serialized = Instantiate(Resources.Load<GameObject>("SerializedRig"));
        var serializedSkin = serialized.GetComponent<SkinnedMeshRenderer>();
        Bounds("serialized/loaded", serializedSkin);
        var clone = Instantiate(serialized);
        Bounds("serialized/clone", clone.GetComponent<SkinnedMeshRenderer>());
        serializedSkin.ResetLocalBounds();
        Bounds("serialized/reset-local",serializedSkin);
        Destroy(serialized);
        Destroy(clone);
        int layer = 1;
        foreach (bool animatedRoot in new[] {false,true})
        foreach (var mode in new[] { AnimatorCullingMode.AlwaysAnimate, AnimatorCullingMode.CullUpdateTransforms, AnimatorCullingMode.CullCompletely })
        foreach (bool offscreen in new[] {false,true})
        foreach (bool explicitBounds in new[] {false,true})
        {
            var rig = new GameObject("Rig").transform;
            var root = new GameObject("Bone").transform;
            root.SetParent(rig,false);
            var skin = Skin("Skin",rig,root);
            skin.gameObject.layer = layer;
            skin.transform.localPosition = new Vector3(20,0,0);
            skin.updateWhenOffscreen = offscreen;
            if (explicitBounds) skin.localBounds = new Bounds(Vector3.zero,new Vector3(2,2,2));
            var marker = new GameObject("Marker").transform;
            marker.SetParent(rig,false);
            var animator = rig.gameObject.AddComponent<Animator>();
            animator.cullingMode = mode;
            animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(animatedRoot ? "RootSweep" : "Culling");
            var target = new RenderTexture(64,36,24);
            target.Create();
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0,0,-10);
            camera.orthographic = true;
            camera.orthographicSize = 3;
            camera.aspect = 64f/36f;
            camera.cullingMask = 1 << layer++;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.targetTexture = target;
            cases.Add(new Case {key=(animatedRoot ? "animated/" : "")+mode+"/"+B(offscreen)+"/"+B(explicitBounds),skin=skin,root=root,marker=marker,animator=animator,
                animatedRoot=animatedRoot,camera=camera,target=target,pixels=new Texture2D(64,36,TextureFormat.RGB24,false),offscreen=offscreen,explicitBounds=explicitBounds});
        }
    }
    void Update()
    {
        if (failed) return;
        try
        {
            if (frame == 1) Build();
            foreach (var c in cases)
            {
                // Move the bone through the right camera edge independently of the renderer.
                float x = frame < 6 ? 0 : frame < 10 ? 5 : frame < 16 ? 12 : 0;
                if (!c.animatedRoot) c.root.localPosition = new Vector3(x,0,0);
                else c.camera.transform.position = new Vector3(frame < 22 ? 0 : 12,0,-10);
                if (!c.animatedRoot && frame == 20) c.skin.rootBone = null;
                if (!c.animatedRoot && frame == 23) c.skin.rootBone = c.root;
                if (frame == 26) c.skin.ResetLocalBounds();
                if (frame == 27) c.skin.localBounds = new Bounds(new Vector3(1,0,0), new Vector3(3,3,3));
                if (frame == 28) c.skin.updateWhenOffscreen = !c.offscreen;
                Bounds("u/"+c.key+"/"+frame,c.skin);
            }
        }
        catch (Exception e) { Fail(e); }
    }
    void LateUpdate()
    {
        try
        {
            foreach (var c in cases)
            {
                Bounds("late/"+c.key+"/"+frame,c.skin);
                if (frame > 2)
                {
                    var prior = RenderTexture.active;
                    RenderTexture.active=c.target;
                    c.pixels.ReadPixels(new Rect(0,0,64,36),0,0);
                    c.pixels.Apply();
                    RenderTexture.active=prior;
                    bool present = false;
                    foreach(var p in c.pixels.GetPixels32()) if(p.r>128) {present=true;break;}
                    Put("render/"+c.key+"/"+frame,"{\"visible\":"+B(c.skin.isVisible)+",\"image\":"+B(present)
                        +",\"marker\":"+V(c.animatedRoot ? c.root.localPosition : c.marker.localPosition)+",\"time\":"+F(c.animator.GetCurrentAnimatorStateInfo(0).normalizedTime)+"}");
                }
            }
        }
        catch(Exception e) {Fail(e);}
        if (++frame < 30 && !failed) return;
        Put("complete",B(!failed));
        File.WriteAllText(Path.Combine(directory,"values.jsonl"),records.ToString());
        enabled=false;
        if(finished!=null) finished(failed?1:0);
        else UnityEditor.EditorApplication.Exit(failed?1:0);
    }
    void Start()
    {
        directory=Environment.GetEnvironmentVariable("SKINNEDBOUNDS_PROBE_OUT");
        Directory.CreateDirectory(directory);
        material = new Material(Shader.Find("Unlit/Color"));
        material.color=Color.white;
    }
    void Fail(Exception e)
    {
        Put("fatal", "\""+e.ToString().Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","\\r")+"\"");
        failed=true;
    }
    public void OnClipEvent() {}
}
