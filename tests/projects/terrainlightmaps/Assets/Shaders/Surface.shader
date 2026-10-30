Shader "TerrainLightmaps/Surface" {
SubShader { Tags { "RenderType"="Opaque" } LOD 200
CGPROGRAM
#pragma surface surf Lambert
struct Input { float2 uv_MainTex; };
sampler2D _MainTex;
void surf(Input IN,inout SurfaceOutput o) { o.Albedo=float3(.7,.55,.4); }
ENDCG
} Fallback "Diffuse"
}
