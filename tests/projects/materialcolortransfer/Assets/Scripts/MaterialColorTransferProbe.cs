using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
public class MaterialColorTransferProbe:MonoBehaviour {
 public static readonly string[] Properties={"_Color","_Hdr","_Vector","_GammaColor","_GammaHdr","_GammaVector","_GammaFloat","_Float"};
 static readonly Vector4[] Inputs={new Vector4(.5f,.04045f,.0031308f,.25f),new Vector4(2,4,8,3),new Vector4(-.5f,-.04045f,-2,-3),new Vector4(0,1,1.0001f,1.0001f),new Vector4(.9999f,16,64,-.25f),new Vector4(.04044999f,.04045001f,.0031308001f,2)};
 readonly StringBuilder rows=new StringBuilder();string output;Mesh mesh;RenderTexture target;
 static string Number(float x)=>float.IsNaN(x)?"\"NaN\"":float.IsPositiveInfinity(x)?"\"+Infinity\"":float.IsNegativeInfinity(x)?"\"-Infinity\"":x.ToString("R",CultureInfo.InvariantCulture);
 void Value(string key,string json){rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(json).Append("}\n");}
 void Vector(string key,Vector4 x){Value(key,"["+Number(x.x)+","+Number(x.y)+","+Number(x.z)+","+Number(x.w)+"]");}
 void ReadCpu(string key,Material m,MaterialPropertyBlock b=null) {
  for(int i=0;i<Properties.Length;i++) {
   string n=Properties[i];if(i<6){Vector(key+n+".color",b==null?m.GetColor(n):b.GetColor(n));Vector(key+n+".vector",b==null?m.GetVector(n):b.GetVector(n));}
   else Value(key+n+".float",Number(b==null?m.GetFloat(n):b.GetFloat(n)));
  }
 }
 void Assign(Material m,MaterialPropertyBlock b,Vector4 v,bool color) {
  for(int i=0;i<Properties.Length;i++) {
   string n=Properties[i];
   if(i>=6){if(b==null)m.SetFloat(n,v.x);else b.SetFloat(n,v.x);}
   else if(b==null){if(color)m.SetColor(n,v);else m.SetVector(n,v);}
   else {if(color)b.SetColor(n,v);else b.SetVector(n,v);}
  }
 }
 void Capture(string key,Material m,MaterialPropertyBlock b=null) {
  ReadCpu(key+".before",m,b);
  for(int i=0;i<Properties.Length;i++) {
   string k=key+Properties[i];m.SetFloat("_Select",i);if(b!=null)b.SetFloat("_Select",i);
   var cmd=new CommandBuffer();cmd.SetRenderTarget(target);cmd.ClearRenderTarget(false,true,Color.clear);
   cmd.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);cmd.DrawMesh(mesh,Matrix4x4.identity,m,0,0,b);
   Graphics.ExecuteCommandBuffer(cmd);cmd.Release();
   RenderTexture.active=target;var image=new Texture2D(4,4,TextureFormat.RGBAFloat,false,true);image.ReadPixels(new Rect(0,0,4,4),0,0);image.Apply();
   using(var w=new BinaryWriter(File.Create(Path.Combine(output,k+".rgba"))))foreach(var c in image.GetPixels()){w.Write(c.r);w.Write(c.g);w.Write(c.b);w.Write(c.a);}
   Value(k+".pixels","16");Vector(k+".gpu",image.GetPixel(2,2));DestroyImmediate(image);RenderTexture.active=null;
  }
  ReadCpu(key+".after",m,b);
 }
 IEnumerator Start() {
  yield return null;
  output=Environment.GetEnvironmentVariable("MATERIAL_COLOR_TRANSFER_OUT");Directory.CreateDirectory(output);
  Value("colorSpace",((int)QualitySettings.activeColorSpace).ToString());
  target=new RenderTexture(4,4,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.antiAliasing=1;target.Create();
  Value("target","["+(int)target.graphicsFormat+","+target.antiAliasing+","+(target.sRGB?1:0)+"]");
  mesh=new Mesh();mesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};mesh.triangles=new[]{0,1,2,0,2,3};
  var m=new Material(Resources.Load<Shader>("Transfer"));Capture("default",m);
  var imported=Resources.Load<Material>("Imported");var variant=Resources.Load<Material>("Variant");
  Capture("imported",imported);Capture("variant",variant);
  foreach(bool color in new[]{true,false})for(int i=0;i<Inputs.Length;i++) {
   var input=Inputs[i];string k=(color?"setColor":"setVector")+i;Vector(k+".source",input);Assign(m,null,input,color);Capture(k,m);
   var copy=new Material(m);Capture(k+".copy",copy);DestroyImmediate(copy);
   var block=new MaterialPropertyBlock();Assign(m,block,input,color);ReadCpu(k+".materialSource",m);Capture(k+".block",m,block);ReadCpu(k+".materialPreserved",m);
   var go=new GameObject("Identity");var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=m;r.SetPropertyBlock(block);var stored=new MaterialPropertyBlock();r.GetPropertyBlock(stored);
   block.Clear();ReadCpu(k+".stored",m,stored);Value(k+".sharedIdentity",r.sharedMaterial==m?"true":"false");Capture(k+".storedDraw",m,stored);DestroyImmediate(go);
  }
  Assign(imported,null,Inputs[1],true);Capture("parentLive",variant);variant.SetColor("_Color",Inputs[4]);Assign(imported,null,Inputs[2],false);Capture("parentLiveOverride",variant);
  foreach(float x in new[]{-2f,-.04045f,-.0031308f,0f,.00313079f,.0031308f,.00313081f,.04044999f,.04045f,.04045001f,.5f,.9999f,1f,1.0001f,2f,4f,8f,16f,64f,float.NaN,float.NegativeInfinity,float.PositiveInfinity}) {
   string k="math"+Array.IndexOf(new[]{-2f,-.04045f,-.0031308f,0f,.00313079f,.0031308f,.00313081f,.04044999f,.04045f,.04045001f,.5f,.9999f,1f,1.0001f,2f,4f,8f,16f,64f,float.NaN,float.NegativeInfinity,float.PositiveInfinity},x);
   Value(k+".input",Number(x));Value(k+".gammaToLinear",Number(Mathf.GammaToLinearSpace(x)));Value(k+".linearToGamma",Number(Mathf.LinearToGammaSpace(x)));Vector(k+".linear",new Color(x,x,x,3).linear);Vector(k+".gamma",new Color(x,x,x,-2).gamma);
  }
  // Nonfinite values are CPU-only: their GPU behavior is deliberately unmeasured.
  Assign(m,null,new Vector4(float.NaN,float.NegativeInfinity,float.PositiveInfinity,-0f),true);ReadCpu("nonfinite",m);
  File.WriteAllText(Path.Combine(output,"values.jsonl"),rows.ToString());target.Release();DestroyImmediate(target);DestroyImmediate(mesh);DestroyImmediate(m);
  Debug.Log("MATERIAL_COLOR_TRANSFER_DONE "+output);
#if UNITY_EDITOR
  UnityEditor.EditorApplication.Exit(0);
#else
  Application.Quit();
#endif
 }
}
