using System;
using System.IO;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class TerrainLightmapProbe : MonoBehaviour
{
    string output; readonly StringBuilder records=new StringBuilder(); Camera camera; Terrain a,b; Material diagnostic;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() { new GameObject("TerrainLightmapProbe").AddComponent<TerrainLightmapProbe>(); }
    static string F(float f)=>f.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    void Record(string key, params float[] values) { records.Append("{\"key\":\"").Append(key).Append("\",\"value\":["); for(int i=0;i<values.Length;i++) { if(i>0)records.Append(','); records.Append(F(values[i])); } records.Append("]}\n"); }
    void Fields(string key,Terrain t) { var st=t.lightmapScaleOffset; var rt=t.realtimeLightmapScaleOffset; Record(key,t.lightmapIndex,t.realtimeLightmapIndex,st.x,st.y,st.z,st.w,rt.x,rt.y,rt.z,rt.w); }
    void Capture(string key) {
        var target=RenderTexture.GetTemporary(128,64,24,RenderTextureFormat.ARGB32);
        camera.targetTexture=target; camera.Render(); RenderTexture.active=target;
        var tex=new Texture2D(128,64,TextureFormat.RGBA32,false); tex.ReadPixels(new Rect(0,0,128,64),0,0); tex.Apply();
        File.WriteAllBytes(Path.Combine(output,key+".png"),tex.EncodeToPNG());
        foreach(int y in new[]{8,24,40,56}) foreach(int x in new[]{8,24,40,56,72,88,104,120}) { var c=tex.GetPixel(x,y); Record(key+"/"+x+","+y,c.r,c.g,c.b,c.a); }
        camera.targetTexture=null; RenderTexture.active=null; RenderTexture.ReleaseTemporary(target); DestroyImmediate(tex);
    }
    static Texture2D Map(bool gradient,Color color) { var t=new Texture2D(16,16,TextureFormat.RGBAFloat,false,true) { wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear }; var p=new Color[256]; for(int y=0;y<16;y++) for(int x=0;x<16;x++) p[y*16+x]=gradient?new Color((x+.5f)/16,(y+.5f)/16,.125f,1):color; t.SetPixels(p); t.Apply(); return t; }
    IEnumerator Start() {
        yield return null; yield return null; yield return null;
        output=Environment.GetEnvironmentVariable("TERRAIN_LIGHTMAP_OUT"); if(string.IsNullOrEmpty(output)) yield break; Directory.CreateDirectory(output);
        camera=Camera.main; a=GameObject.Find("Baked Terrain").GetComponent<Terrain>();
        Fields("scene",a); Record("scene/maps",LightmapSettings.lightmaps.Length);
        Capture("00-baked");
        var clone=Instantiate(a.gameObject); Fields("scene/clone",clone.GetComponent<Terrain>()); clone.SetActive(false);
        var prefab=Resources.Load<GameObject>("BoundTerrain"); Fields("prefab",prefab.GetComponent<Terrain>());
        var pc=Instantiate(prefab); Fields("prefab/clone",pc.GetComponent<Terrain>()); pc.SetActive(false);
        var fresh=new GameObject("Fresh",typeof(Terrain)).GetComponent<Terrain>(); Fields("defaults",fresh); DestroyImmediate(fresh.gameObject);
        b=Terrain.CreateTerrainGameObject(a.terrainData).GetComponent<Terrain>(); b.transform.position=new Vector3(4,0,0); b.drawInstanced=false;
        foreach(var light in FindObjectsByType<Light>(FindObjectsSortMode.None)) light.enabled=false;
        RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=Color.black; RenderSettings.reflectionIntensity=0; RenderSettings.fog=false;
        diagnostic=new Material(Shader.Find("TerrainLightmaps/Diagnostic")); a.materialTemplate=b.materialTemplate=diagnostic;
        var red=Map(false,new Color(.125f,.25f,.5f,1)); var green=Map(false,new Color(.5f,.125f,.25f,1)); var gradient=Map(true,Color.black); var direction=Map(false,new Color(.5f,1,.5f,.75f));
        LightmapSettings.lightmapsMode=LightmapsMode.NonDirectional;
        LightmapSettings.lightmaps=new[]{new LightmapData{lightmapColor=red},new LightmapData{lightmapColor=green},new LightmapData{lightmapColor=gradient},new LightmapData()};
        a.lightmapIndex=0; b.lightmapIndex=1; a.lightmapScaleOffset=b.lightmapScaleOffset=new Vector4(1,1,0,0);
        // New templates avoid testing unrelated cached-template mutation behavior.
        for(int mode=0;mode<=6;mode++) { diagnostic=new Material(diagnostic); diagnostic.SetFloat("_Mode",mode); a.materialTemplate=b.materialTemplate=diagnostic; Capture("01-mode"+mode); }
        a.lightmapIndex=b.lightmapIndex=2; a.lightmapScaleOffset=new Vector4(.5f,.25f,.125f,.5f); b.lightmapScaleOffset=new Vector4(.25f,.5f,.625f,.125f);
        diagnostic=new Material(diagnostic); diagnostic.SetFloat("_Mode",4); a.materialTemplate=b.materialTemplate=diagnostic; Capture("02-transform");
        diagnostic=new Material(diagnostic); diagnostic.SetFloat("_Mode",5); a.materialTemplate=b.materialTemplate=diagnostic; Capture("03-gradient");
        a.lightmapIndex=1; b.lightmapIndex=0; Capture("04-live-indices");
        LightmapSettings.lightmaps=new[]{new LightmapData{lightmapColor=green},new LightmapData{lightmapColor=red},new LightmapData{lightmapColor=gradient},new LightmapData()}; Capture("05-live-maps");
        foreach(int index in new[]{-2,-1,65534,65535,65536,3,20}) { a.lightmapIndex=b.lightmapIndex=index; Fields("index/"+index,a); Capture("06-index"+index); }
        a.lightmapIndex=0; b.lightmapIndex=1; b.enabled=false; Capture("07-disabled"); b.enabled=true; b.drawHeightmap=false; Capture("08-no-heightmap"); b.drawHeightmap=true;
        a.realtimeLightmapIndex=5; a.realtimeLightmapScaleOffset=new Vector4(.3f,.4f,.2f,.1f); a.lightmapIndex=2; Fields("runtime",a);
        clone=Instantiate(a.gameObject); Fields("runtime/clone",clone.GetComponent<Terrain>()); clone.SetActive(false);
        a.lightmapIndex=b.lightmapIndex=0;
        LightmapSettings.lightmaps=new[]{new LightmapData{lightmapColor=red,lightmapDir=direction},new LightmapData{lightmapColor=green}};
        diagnostic=new Material(diagnostic); diagnostic.SetFloat("_Mode",6); a.materialTemplate=b.materialTemplate=diagnostic;
        LightmapSettings.lightmapsMode=LightmapsMode.CombinedDirectional; Capture("09-directional"); b.lightmapIndex=1; Capture("10-missing-direction");
        LightmapSettings.lightmapsMode=LightmapsMode.NonDirectional; Capture("11-nondirectional");
        var builtin=new Material(Shader.Find("Nature/Terrain/Diffuse")); a.materialTemplate=b.materialTemplate=builtin;
        a.lightmapIndex=0; b.lightmapIndex=1; a.lightmapScaleOffset=b.lightmapScaleOffset=new Vector4(1,1,0,0); Capture("12-builtin-colors");
        LightmapSettings.lightmapsMode=LightmapsMode.CombinedDirectional; Capture("13-builtin-directional");
        LightmapSettings.lightmapsMode=LightmapsMode.NonDirectional; a.lightmapIndex=b.lightmapIndex=2; LightmapSettings.lightmaps=new[]{new LightmapData{lightmapColor=red},new LightmapData{lightmapColor=green},new LightmapData{lightmapColor=gradient}};
        a.lightmapScaleOffset=new Vector4(.5f,.5f,0,.5f); b.lightmapScaleOffset=new Vector4(.5f,.5f,.5f,0); Capture("14-builtin-gradient");
        File.WriteAllText(Path.Combine(output,"records.jsonl"),records.ToString()); Debug.Log("Terrain lightmaps probe complete: "+output);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
