Shader "Probe/WarmupMixed"
{
 SubShader
 {
  Cull Off ZWrite Off ZTest Always
  HLSLINCLUDE
  #pragma target 4.5
  Texture2D<uint4> _UnsignedTex;
  Texture2D<int4> _SignedTex;
  float4 loadUint() : SV_Target { return (float4)_UnsignedTex.Load(int3(0,0,0)); }
  float4 loadSint() : SV_Target { return (float4)_SignedTex.Load(int3(0,0,0)); }
  float4 vert(float4 p : POSITION) : SV_POSITION { return p; }
  float4 fragFloat() : SV_Target { return float4(.125,.375,.625,1); }
  uint4 fragUint() : SV_Target { return uint4(7,11,13,17); }
  int4 fragSint() : SV_Target { return int4(-7,11,-13,17); }
  ENDHLSL
  Pass { HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment fragFloat
   ENDHLSL }
  Pass { HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment fragUint
   ENDHLSL }
  Pass { HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment fragSint
   ENDHLSL }
  Pass { HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment loadUint
   ENDHLSL }
  Pass { HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment loadSint
   ENDHLSL }
 }
}
