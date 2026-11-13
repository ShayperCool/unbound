Shader "Probe/SamplerStates/DepthState" {
 Properties { _MainTex ("Main", 2D) = "white" {} }
 SubShader { Pass {
  ZTest Always ZWrite Off Cull Off
  CGPROGRAM
  #pragma target 4.5
  #pragma vertex vert_img
  #pragma fragment frag
  #include "UnityCG.cginc"
  Texture2D _Depth;
  Texture2D _OtherDepth;
  SamplerComparisonState sampler_Depth;
  SamplerComparisonState sampler_OtherDepth;
  SamplerComparisonState sampler_LinearClampCompare;
  float4 frag(v2f_img input) : SV_Target {
   float2 uv = float2(.4,.5);
   return float4(_Depth.SampleCmpLevelZero(sampler_Depth,uv,.5),
    _Depth.SampleCmpLevelZero(sampler_OtherDepth,uv,.5),
    _Depth.SampleCmpLevelZero(sampler_LinearClampCompare,uv,.5),
    _OtherDepth.SampleCmpLevelZero(sampler_OtherDepth,uv,.1));
  }
  ENDCG
 } }
}
