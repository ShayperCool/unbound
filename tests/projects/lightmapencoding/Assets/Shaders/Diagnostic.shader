Shader "LightmapEncoding/Diagnostic" {
Properties { _Mode ("Mode", Float) = 0 _Cube ("Cube", Cube) = "" {} _Flat ("Flat", 2D) = "white" {} }
SubShader { Tags { "RenderType"="Opaque" }
Pass { Tags { "LightMode"="ForwardBase" } Cull Off
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile __ LIGHTMAP_ON
#include "UnityCG.cginc"
float _Mode;
samplerCUBE _Cube; float4 _Cube_HDR;
sampler2D _Flat; float4 _Flat_HDR;
struct v2f { float4 pos:SV_POSITION; float2 uv1:TEXCOORD0; float2 cell:TEXCOORD1; };
v2f vert(appdata_full v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv1=v.texcoord1.xy; o.cell=v.texcoord.xy*float2(16,8); return o; }
// Row = cube face (+X, -X, +Y, -Y, +Z, -Z), column = texel s + 4t of its 4x4 face, at the texel's centre.
float3 Direction(float2 cell) {
 float face=floor(cell.y); float k=floor(cell.x);
 float u=((fmod(k,4)+0.5)/4)*2-1; float v=((floor(k/4)+0.5)/4)*2-1;
 if (face<0.5) return float3(1,-v,-u);
 if (face<1.5) return float3(-1,-v,u);
 if (face<2.5) return float3(u,1,v);
 if (face<3.5) return float3(u,-1,-v);
 if (face<4.5) return float3(u,-v,1);
 return float3(-u,-v,-1);
}
float4 frag(v2f i):SV_Target {
 if (_Mode > 5.5) {
  // Material textures: their decode values, the cube decoded with them, a lightmap's level 1.
  if (_Mode < 6.5) return _Cube_HDR;
  if (_Mode < 7.5) {
   if (i.cell.y >= 6) return float4(0,0,0,0);
   return float4(DecodeHDR(texCUBElod(_Cube,float4(Direction(i.cell),0)),_Cube_HDR),1);
  }
  if (_Mode < 8.5) return _Flat_HDR;
  #ifdef LIGHTMAP_ON
   return UNITY_SAMPLE_TEX2D_LOD(unity_Lightmap,i.uv1*unity_LightmapST.xy+unity_LightmapST.zw,1);
  #else
   return float4(1,0,1,1);
  #endif
 }
 if (_Mode > 2.5) {
  if (i.cell.y >= 6) return float4(0,0,0,0);
  float4 raw=UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0,Direction(i.cell),0);
  if (_Mode < 3.5) return raw;
  if (_Mode < 4.5) return unity_SpecCube0_HDR;
  return float4(DecodeHDR(raw,unity_SpecCube0_HDR),1);
 }
 #ifdef LIGHTMAP_ON
  float2 uv=i.uv1*unity_LightmapST.xy+unity_LightmapST.zw;
  float4 raw=UNITY_SAMPLE_TEX2D(unity_Lightmap,uv);
  if (_Mode < 0.5) return raw;
  if (_Mode < 1.5) return unity_Lightmap_HDR;
  return float4(DecodeLightmap(raw),1);
 #else
  return float4(1,0,1,1);
 #endif
}
ENDCG
} } }
