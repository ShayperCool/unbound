Shader "Probe/NativeGfx/DeviceDepthSample"
{
 SubShader { Pass {
  ZTest Always Cull Off ZWrite Off
  CGPROGRAM
  #pragma target 3.5
  #pragma vertex vert_img
  #pragma fragment frag
  #include "UnityCG.cginc"
  Texture2D _Depth; SamplerState sampler_Depth;
  float4 frag(v2f_img i) : SV_Target { return _Depth.Sample(sampler_Depth, float2(.1,.5)); }
  ENDCG
 } }
}
