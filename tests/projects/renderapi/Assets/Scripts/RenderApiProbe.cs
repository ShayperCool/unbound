using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

public sealed partial class RenderApiProbe : MonoBehaviour
{
    public static Action<int> finished;
    readonly StringBuilder records = new StringBuilder();
    string directory;
    Camera camera;
    int added, removed;
    bool drawGizmos;
    bool drewGizmos;
    public static Action<bool> setGameGizmos;
    static readonly Vector3[] Vertices = { new Vector3(-.8f,-.6f,0), new Vector3(.8f,-.6f,0), new Vector3(0,.8f,0) };
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("RENDER_API_OUT") != null)
            new GameObject("RenderApi").AddComponent<RenderApiProbe>();
    }
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string F(float f) => float.IsNaN(f) || float.IsInfinity(f) ? Q(f.ToString(CultureInfo.InvariantCulture)) : f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "["+F(v.x)+","+F(v.y)+","+F(v.z)+"]";
    static string C(Color c) => "["+F(c.r)+","+F(c.g)+","+F(c.b)+","+F(c.a)+"]";
    void Value(string k, string v) { records.Append("{\"k\":").Append(Q(k)).Append(",\"v\":").Append(v).Append("}\n"); File.WriteAllText(Path.Combine(directory,"values.jsonl"),records.ToString()); }
    void Bool(string k, bool b) => Value(k, b ? "true" : "false");
    void Try(string k, Action action)
    {
        File.AppendAllText(Path.Combine(directory,"progress.txt"),k+"\n");
        try { action(); Value(k, Q("ok")); }
        catch (Exception e) { Value(k, Q(e.GetType().Name+": "+e.Message)); }
    }
    Mesh Make()
    {
        var mesh = new Mesh { name = "probe mesh", vertices = Vertices, triangles = new[] {0,1,2} };
        return mesh;
    }
    void ClearIndexPadding(Mesh mesh)
    {
        // Raw 16-bit buffers can include an extra index for word alignment. Initialize that
        // unused storage so captures never depend on its previous allocation contents.
        if (mesh.indexFormat != IndexFormat.UInt16) return;
        int used = 0;
        for (int sub = 0; sub < mesh.subMeshCount; sub++) used = Math.Max(used, (int)(mesh.GetIndexStart(sub) + mesh.GetIndexCount(sub)));
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
            if (mesh.GetTopology(sub) == MeshTopology.Quads) used += (int)mesh.GetIndexCount(sub) / 4 * 6;
        using (var buffer = mesh.GetIndexBuffer())
        {
            if (buffer.count <= used) return;
            var values = new ushort[buffer.count]; buffer.GetData(values);
            Array.Clear(values, used, values.Length - used); buffer.SetData(values);
        }
    }
    void Buffer(string key, GraphicsBuffer b, bool data = true)
    {
        Bool(key+".null", b == null);
        if (b == null) return;
        Bool(key+".valid", b.IsValid());
        Try(key+".properties", () => Value(key+".info", "["+b.count+","+b.stride+","+(int)b.target+","+(int)b.usageFlags+"]"));
        if (data) Try(key+".read", () =>
        {
            var bytes = new byte[b.count*b.stride];
            for(int n=0;n<bytes.Length;n++)bytes[n]=0x5a;
            b.GetData(bytes);
            Value(key+".bytes", Q(Convert.ToBase64String(bytes)));
        });
    }
    void MeshCases()
    {
        var empty = new Mesh { name = "empty" };
        Value("mesh.default.targets", "["+(int)empty.vertexBufferTarget+","+(int)empty.indexBufferTarget+"]");
        foreach (int s in new[] {-1,0,1,4}) Try("mesh.empty.stream"+s, () => { using(var b = empty.GetVertexBuffer(s)) Buffer("mesh.empty.v"+s,b); });
        Try("mesh.empty.index", () => { using(var b = empty.GetIndexBuffer()) Buffer("mesh.empty.i",b); });
        DestroyImmediate(empty);
        foreach (var flags in new[] {(GraphicsBuffer.Target)0, GraphicsBuffer.Target.Raw, GraphicsBuffer.Target.Structured, GraphicsBuffer.Target.Raw|GraphicsBuffer.Target.Structured})
        {
            var mesh = Make(); string k = "mesh.flags"+(int)flags;
            Try(k+".set", () => { mesh.vertexBufferTarget = flags; mesh.indexBufferTarget = flags; });
            Value(k+".targets", "["+(int)mesh.vertexBufferTarget+","+(int)mesh.indexBufferTarget+"]");
            mesh.UploadMeshData(false);
            ClearIndexPadding(mesh);
            Try(k+".vertex", () => { using(var b = mesh.GetVertexBuffer(0)) Buffer(k+".v",b); });
            Try(k+".index", () => { using(var b = mesh.GetIndexBuffer()) Buffer(k+".i",b); });
            DestroyImmediate(mesh);
        }
        var m = Make();
        m.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
        m.indexBufferTarget |= GraphicsBuffer.Target.Raw;
        var v = m.GetVertexBuffer(0); var i = m.GetIndexBuffer(); var alias = m.GetVertexBuffer(0);
        Bool("mesh.alias.sameObject", ReferenceEquals(v,alias));
        Bool("mesh.alias.samePtr", v.GetNativeBufferPtr()==alias.GetNativeBufferPtr());
        v.Dispose(); Bool("mesh.dispose.valid",v.IsValid()); Buffer("mesh.dispose.alias",alias);
        Try("mesh.dispose.twice",()=>v.Dispose());
        Try("mesh.dispose.read",()=>v.GetData(new float[9]));
        Try("mesh.dispose.count",()=>Value("mesh.dispose.countValue",v.count.ToString()));
        Try("mesh.dispose.stride",()=>Value("mesh.dispose.strideValue",v.stride.ToString()));
        Try("mesh.dispose.target",()=>Value("mesh.dispose.targetValue",((int)v.target).ToString()));
        Try("mesh.dispose.ptr",()=>Bool("mesh.dispose.ptrZero",v.GetNativeBufferPtr()==IntPtr.Zero));
        using(var b = m.GetVertexBuffer(0)) Buffer("mesh.dispose.reacquired",b);
        alias.Dispose(); i.Dispose();
        foreach (int s in new[] {-1,1,2,4}) Try("mesh.invalid.stream"+s,()=>{ using(var b=m.GetVertexBuffer(s)) Buffer("mesh.invalid.v"+s,b); });
        // A layout change, a same-size edit and an explicit upload each have their own lifetime control.
        var previous = m.GetVertexBuffer(0); var oldPtr = previous.GetNativeBufferPtr();
        m.vertices = new[] {new Vector3(-.2f,-.6f,0),Vertices[1],Vertices[2]};
        Bool("mesh.edit.beforeUpload.valid",previous.IsValid());
        m.UploadMeshData(false);
        Bool("mesh.edit.afterUpload.valid",previous.IsValid());
        using(var b=m.GetVertexBuffer(0)) { Bool("mesh.edit.samePtr",b.GetNativeBufferPtr()==oldPtr); Buffer("mesh.edit.current",b); }
        previous.Dispose();
        previous = m.GetVertexBuffer(0); oldPtr = previous.GetNativeBufferPtr();
        m.normals = new[] {Vector3.forward,Vector3.forward,Vector3.forward};
        m.UploadMeshData(false);
        Bool("mesh.layout.old.valid",previous.IsValid());
        Try("mesh.layout.old.count",()=>Value("mesh.layout.old.countValue",previous.count.ToString()));
        Try("mesh.layout.old.ptr",()=>Bool("mesh.layout.old.ptrZero",previous.GetNativeBufferPtr()==IntPtr.Zero));
        Buffer("mesh.layout.invalidated",previous);
        using(var b=m.GetVertexBuffer(0)) { Bool("mesh.layout.samePtr",b.GetNativeBufferPtr()==oldPtr); Buffer("mesh.layout.current",b); }
        previous.Dispose();
        previous = m.GetVertexBuffer(0);
        m.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
        m.UploadMeshData(false); Bool("mesh.resize.old.valid", previous.IsValid()); previous.Dispose();
        previous = m.GetIndexBuffer(); oldPtr = previous.GetNativeBufferPtr();
        m.triangles = new[] {2,1,0}; m.UploadMeshData(false);
        Bool("mesh.index.edit.old.valid", previous.IsValid());
        using(var b=m.GetIndexBuffer()) Bool("mesh.index.edit.samePtr",b.GetNativeBufferPtr()==oldPtr);
        previous.Dispose();
        m.UploadMeshData(true); ClearIndexPadding(m); Bool("mesh.unreadable",m.isReadable);
        Try("mesh.unreadable.vertex",()=>{ using(var b=m.GetVertexBuffer(0)) Buffer("mesh.unreadable.v",b); });
        Try("mesh.unreadable.index",()=>{ using(var b=m.GetIndexBuffer()) Buffer("mesh.unreadable.i",b); });
        previous=m.GetVertexBuffer(0); var previousIndex=m.GetIndexBuffer();
        DestroyImmediate(m); Bool("mesh.destroy.vertex.valid",previous.IsValid()); Bool("mesh.destroy.index.valid",previousIndex.IsValid());
        Buffer("mesh.destroy.retained.vertex",previous);Buffer("mesh.destroy.retained.index",previousIndex);previous.Dispose(); previousIndex.Dispose();
        m = new Mesh {name="multi"};
        m.SetVertexBufferParams(3,new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3,0),new VertexAttributeDescriptor(VertexAttribute.Color,VertexAttributeFormat.UNorm8,4,1));
        m.SetVertexBufferData(Vertices,0,0,3,0);
        m.SetVertexBufferData(new[] {new Color32(1,2,3,4),new Color32(5,6,7,8),new Color32(9,10,11,12)},0,0,3,1);
        m.SetIndexBufferParams(3,IndexFormat.UInt32); m.SetIndexBufferData(new uint[] {2,1,0},0,0,3);
        m.UploadMeshData(false);
        for(int s=0;s<2;s++) { using(var b=m.GetVertexBuffer(s)) Buffer("mesh.multi.v"+s,b); }
        using(var b=m.GetIndexBuffer()) Buffer("mesh.multi.i",b);
        DestroyImmediate(m);
        m=Make(); m.vertexBufferTarget|=GraphicsBuffer.Target.Raw; m.indexBufferTarget|=GraphicsBuffer.Target.Raw;
        ClearIndexPadding(m);
        var compute=Resources.Load<ComputeShader>("MeshRead");
        using(var output=new GraphicsBuffer(GraphicsBuffer.Target.Structured,4,4))
        {
            var vb=m.GetVertexBuffer(0);var ib=m.GetIndexBuffer();
            compute.SetBuffer(0,"Vertices",vb);compute.SetBuffer(0,"Indices",ib);compute.SetBuffer(0,"Output",output);
            vb.Dispose();ib.Dispose();compute.Dispatch(0,1,1,1);
            var words=new uint[4];output.GetData(words);
            Value("mesh.compute.words","["+string.Join(",",words)+"]");
        }
        DestroyImmediate(m);
        PrecisionCases();
    }
    void PrecisionCases()
    {
        var mesh=new Mesh {name="precise"};
        mesh.vertexBufferTarget|=GraphicsBuffer.Target.CopySource;
        mesh.SetVertexBufferParams(2,
            new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3,0),
            new VertexAttributeDescriptor(VertexAttribute.Color,VertexAttributeFormat.Float32,4,1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.UInt32,4,2));
        mesh.SetVertexBufferData(new[]{Vector3.zero,Vector3.one},0,0,2,0);
        mesh.SetVertexBufferData(new[]{new Vector4(-1,.5f,2,4),new Vector4(.3f,.7f,8,-2)},0,0,2,1);
        mesh.SetVertexBufferData(new uint[]{0xffffffff,16777217,0x80000001,17,31,0x7fffffff,0xabcdef01,99},0,0,8,2);
        using(var colors=mesh.GetVertexBuffer(1))Buffer("mesh.precise.colors",colors);
        using(var integers=mesh.GetVertexBuffer(2))Buffer("mesh.precise.integers",integers);
        mesh.SetIndices(new[]{0,1},MeshTopology.Points,0);
        using(var colors=mesh.GetVertexBuffer(1))Buffer("mesh.precise.afterIndex",colors);
        mesh.vertices=new[]{Vector3.right,Vector3.up};
        using(var colors=mesh.GetVertexBuffer(1))Buffer("mesh.precise.afterPositions",colors);
        Try("mesh.precise.partial.set",()=>mesh.SetVertexBufferData(new byte[]{0xab,0xcd},0,5,2,1));
        using(var colors=mesh.GetVertexBuffer(1))Buffer("mesh.precise.partial",colors);
        var view=mesh.GetVertexBuffer(1);
        using(var destination=new GraphicsBuffer(GraphicsBuffer.Target.Structured|GraphicsBuffer.Target.CopyDestination,view.count,view.stride))
        using(var commands=new CommandBuffer())
        {
            commands.CopyBuffer(view,destination);
            view.Dispose();
            Try("mesh.precise.command.execute",()=>Graphics.ExecuteCommandBuffer(commands));
            Buffer("mesh.precise.command.copy",destination);
        }
        DestroyImmediate(mesh);
    }
    void Sorting()
    {
        SortingLayer.onLayerAdded += OnAdded; SortingLayer.onLayerRemoved += OnRemoved;
        var layers = SortingLayer.layers;
        Value("sorting.count",layers.Length.ToString());
        for(int n=0;n<layers.Length;n++)
        {
            var l=layers[n]; string k="sorting.layer"+n;
            Value(k+".name",Q(l.name)); Value(k+".id",l.id.ToString()); Value(k+".value",l.value.ToString());
            Bool(k+".valid",SortingLayer.IsValid(l.id));
            Value(k+".byName",SortingLayer.NameToID(l.name).ToString());
            Value(k+".valueByName",SortingLayer.GetLayerValueFromName(l.name).ToString());
        }
        foreach(int id in new[] {0,-1,123456})
        {
            string k="sorting.id"+id; Bool(k+".valid",SortingLayer.IsValid(id));
            Value(k+".name",Q(SortingLayer.IDToName(id))); Value(k+".value",SortingLayer.GetLayerValueFromID(id).ToString());
        }
        foreach(string name in new[] {"Default","missing", "", null})
            Try("sorting.name."+(name??"null"),()=>Value("sorting.name."+(name??"null")+".id",SortingLayer.NameToID(name).ToString()));
    }
    void OnAdded(SortingLayer l) { added++; }
    void OnRemoved(SortingLayer l) { removed++; }
    void GizmoCases()
    {
        Value("gizmos.default.color",C(Gizmos.color));
        Value("gizmos.default.matrix",Q(Gizmos.matrix.ToString("R")));
        Bool("gizmos.default.exposure",Gizmos.exposure==null); Value("gizmos.probeSize",F(Gizmos.probeSize));
        Try("gizmos.construct",()=>{new Gizmos();});
        Try("gizmos.color.set",()=>Gizmos.color=new Color(.2f,.3f,.4f,.5f)); Value("gizmos.color",C(Gizmos.color));
        foreach(float channel in new[] {.1f,.3f,.5f,.7f,.9f,-1f,2f})
        {
            Gizmos.color=new Color(channel,channel,channel,channel);
            Value("gizmos.color."+F(channel),C(Gizmos.color));
        }
        var mat=Matrix4x4.TRS(new Vector3(1,2,3),Quaternion.identity,new Vector3(2,3,4));
        Try("gizmos.matrix.set",()=>Gizmos.matrix=mat); Bool("gizmos.matrix.kept",Gizmos.matrix==mat);
        var texture=new Texture2D(1,1);
        Try("gizmos.exposure.set",()=>Gizmos.exposure=texture); Bool("gizmos.exposure.kept",Gizmos.exposure==texture);
        Gizmos.exposure=null; Gizmos.matrix=Matrix4x4.identity;
        Try("gizmos.lod",()=>Value("gizmos.lod.value",F(Gizmos.CalculateLOD(Vector3.zero,1))));
        Try("gizmos.line",()=>Gizmos.DrawLine(Vector3.zero,Vector3.one));
        Try("gizmos.ray",()=>Gizmos.DrawRay(new Ray(Vector3.zero,Vector3.up)));
        Try("gizmos.cube",()=>Gizmos.DrawCube(Vector3.zero,Vector3.one));
        Try("gizmos.wireCube",()=>Gizmos.DrawWireCube(Vector3.zero,Vector3.one));
        Try("gizmos.sphere",()=>Gizmos.DrawSphere(Vector3.zero,-1));
        Try("gizmos.wireSphere",()=>Gizmos.DrawWireSphere(Vector3.zero,0));
        Try("gizmos.mesh.null",()=>Gizmos.DrawMesh(null));
        Try("gizmos.wireMesh.null",()=>Gizmos.DrawWireMesh(null,99));
        Try("gizmos.icon.null",()=>Gizmos.DrawIcon(Vector3.zero,null));
        Try("gizmos.icon.missing",()=>Gizmos.DrawIcon(Vector3.zero,"missing.png"));
        Try("gizmos.texture.null",()=>Gizmos.DrawGUITexture(new Rect(),null));
        Try("gizmos.frustum",()=>Gizmos.DrawFrustum(Vector3.zero,60,10,.1f,1));
        Try("gizmos.list.empty",()=>Gizmos.DrawLineList(Array.Empty<Vector3>()));
        Try("gizmos.list.odd",()=>Gizmos.DrawLineList(new[] {Vector3.zero,Vector3.one,Vector3.up}));
        Try("gizmos.list.even",()=>Gizmos.DrawLineList(new[] {Vector3.zero,Vector3.one}));
        Try("gizmos.strip.empty",()=>Gizmos.DrawLineStrip(Array.Empty<Vector3>(),false));
        Try("gizmos.strip.single",()=>Gizmos.DrawLineStrip(new[] {Vector3.zero},true));
        DestroyImmediate(texture);
    }
    void Image(string key)
    {
        var rt=new RenderTexture(96,64,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear) {antiAliasing=1};
        rt.Create(); camera.targetTexture=rt; camera.Render(); camera.targetTexture=null;
        var saved=RenderTexture.active; RenderTexture.active=rt;
        var t=new Texture2D(96,64,TextureFormat.RGBAFloat,false,true);
        t.ReadPixels(new Rect(0,0,96,64),0,0); t.Apply(); RenderTexture.active=saved;
        SaveImage(key,t);DestroyImmediate(t);rt.Release();DestroyImmediate(rt);
    }
    void SaveImage(string key, Texture2D image)
    {
        using(var w=new BinaryWriter(File.Create(Path.Combine(directory,key+".rgba"))))
            foreach(var c in image.GetPixels()) { w.Write(c.r);w.Write(c.g);w.Write(c.b);w.Write(c.a); }
        Value(key+".pixels",(image.width*image.height).ToString());
    }
    void OnDrawGizmos()
    {
        if(!drawGizmos)return;
        drewGizmos=true;Gizmos.color=Color.red;Gizmos.DrawCube(Vector3.zero,Vector3.one);
    }
    IEnumerator Start()
    {
        directory=Environment.GetEnvironmentVariable("RENDER_API_OUT");Directory.CreateDirectory(directory);
        bool failed=false;
        try { MeshCases();Sorting();GizmoCases(); }
        catch(Exception e) { Value("fatal",Q(e.ToString()));failed=true; }
        camera=new GameObject("camera").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
        camera.orthographic=true;camera.orthographicSize=2;camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=Color.black;camera.allowHDR=false;camera.allowMSAA=false;
        var sprite=new GameObject("sprite").AddComponent<SpriteRenderer>();
        var texture=new Texture2D(8,8,TextureFormat.RGBA32,false); var colors=new Color[64];
        for(int n=0;n<64;n++)colors[n]=Color.white;texture.SetPixels(colors);texture.Apply();
        sprite.sprite=Sprite.Create(texture,new Rect(0,0,8,8),new Vector2(.5f,.5f),8);
        sprite.transform.position=new Vector3(.17f,-.19f,0);
        Value("pixel.default",F(PixelPerfectRendering.pixelSnapSpacing));
        foreach(float spacing in new[] {0f,.25f,.5f,-.25f,float.NaN,float.PositiveInfinity})
        {
            string k="pixel."+F(spacing).Replace("\"","");
            Try(k+".set",()=>PixelPerfectRendering.pixelSnapSpacing=spacing);
            Value(k+".get",F(PixelPerfectRendering.pixelSnapSpacing));
            if(!float.IsNaN(spacing)&&!float.IsInfinity(spacing)) {yield return null;Image(k);Value(k+".position",V(sprite.transform.position));}
        }
        PixelPerfectRendering.pixelSnapSpacing=0; sprite.enabled=false;
        var inputMesh=Make();inputMesh.vertexBufferTarget|=GraphicsBuffer.Target.Raw;inputMesh.indexBufferTarget|=GraphicsBuffer.Target.Raw;
        var material=new Material(Resources.Load<Shader>("MeshRead"));
        using(var vb=inputMesh.GetVertexBuffer(0))material.SetBuffer("MeshVertices",vb);
        using(var ib=inputMesh.GetIndexBuffer())material.SetBuffer("MeshIndices",ib);
        var rt=new RenderTexture(96,64,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);rt.Create();
        var saved=RenderTexture.active;RenderTexture.active=rt;GL.Clear(true,true,Color.black);material.SetPass(0);Graphics.DrawProceduralNow(MeshTopology.Triangles,3);
        var image=new Texture2D(96,64,TextureFormat.RGBAFloat,false,true);image.ReadPixels(new Rect(0,0,96,64),0,0);image.Apply();RenderTexture.active=saved;
        SaveImage("mesh.shader",image);DestroyImmediate(image);rt.Release();DestroyImmediate(rt);DestroyImmediate(material);DestroyImmediate(inputMesh);
        drawGizmos=true;yield return null;Image("gizmos.cameraRender");
        foreach(bool enabled in new[]{false,true})
        {
            setGameGizmos?.Invoke(enabled);drewGizmos=false;
            yield return null;yield return new WaitForEndOfFrame();
            var screen=ScreenCapture.CaptureScreenshotAsTexture();SaveImage("gizmos.game."+enabled,screen);DestroyImmediate(screen);
            Bool("gizmos.game."+enabled+".callback",drewGizmos);
        }
        setGameGizmos?.Invoke(false);
        yield return FollowupCases();
        yield return ExtendedCases();
        PackedCases();
        HdrCases();
        yield return SkinSourceCases();
        yield return SphereMoreCases();
        QuadLayoutCases();
        ColorArrayCases();
        yield return SkinExtraCases();
        AdditionalStreamCases();
        PositionlessStreamCases();
        yield return SkinNormalCases();
        // Runtime queries and scene changes must not synthesize layer edits.
        Value("sorting.added",added.ToString());Value("sorting.removed",removed.ToString());
        SortingLayer.onLayerAdded-=OnAdded;SortingLayer.onLayerRemoved-=OnRemoved;
        Value("complete","true");File.WriteAllText(Path.Combine(directory,"values.jsonl"),records.ToString());
        if(finished!=null)finished(failed?1:0);else Application.Quit(failed?1:0);
    }
}
