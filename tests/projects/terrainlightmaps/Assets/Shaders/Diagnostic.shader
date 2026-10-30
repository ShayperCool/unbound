Shader "TerrainLightmaps/Diagnostic" {
Properties { _Mode ("Mode", Float) = 0 }
SubShader { Tags { "Queue"="Geometry-100" "RenderType"="Opaque" "TerrainCompatible"="True" }
Pass { Tags { "LightMode"="ForwardBase" } Cull Off
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile __ LIGHTMAP_ON
#pragma multi_compile __ DIRLIGHTMAP_COMBINED
#include "UnityCG.cginc"
float _Mode;
struct v2f { float4 pos:SV_POSITION; float2 uv0:TEXCOORD0; float2 uv1:TEXCOORD1; float2 uv2:TEXCOORD2; float2 uv3:TEXCOORD3; };
v2f vert(appdata_full v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv0=v.texcoord.xy; o.uv1=v.texcoord1.xy; o.uv2=v.texcoord2.xy; o.uv3=v.texcoord3.xy; return o; }
float4 frag(v2f i):SV_Target {
 if (_Mode < 0.5) return float4(i.uv0,0,1);
 if (_Mode < 1.5) return float4(i.uv1,0,1);
 if (_Mode < 2.5) return float4(i.uv2,0,1);
 if (_Mode < 3.5) return float4(i.uv3,0,1);
 if (_Mode < 4.5) return float4(i.uv1*unity_LightmapST.xy+unity_LightmapST.zw,0,1);
 #ifdef LIGHTMAP_ON
  float2 uv=i.uv1*unity_LightmapST.xy+unity_LightmapST.zw;
  if (_Mode < 5.5) return float4(UNITY_SAMPLE_TEX2D(unity_Lightmap,uv).rgb,1);
  #ifdef DIRLIGHTMAP_COMBINED
   return float4(UNITY_SAMPLE_TEX2D_SAMPLER(unity_LightmapInd,unity_Lightmap,uv).rgb,1);
  #else
   return float4(0,0,1,1);
  #endif
 #else
  return float4(1,0,1,1);
 #endif
}
ENDCG
} } }
