Shader "Probe/SamplerStates/D16Filter"
{
 SubShader { Pass {
  ZTest Always Cull Off ZWrite Off
  HLSLPROGRAM
  #pragma target 3.5
  #pragma vertex vert_img
  #pragma fragment frag
  #include "UnityCG.cginc"
  Texture2D _Source; SamplerState sampler_Source; float2 _P;
  float4 frag(v2f_img i) : SV_Target { return float4(_Source.Sample(sampler_Source, _P).r, 0, 0, 1); }
  ENDHLSL
 } }
}
