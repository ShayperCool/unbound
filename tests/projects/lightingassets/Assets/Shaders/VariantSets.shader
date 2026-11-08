Shader "Probe/VariantSets"
{
 SubShader
 {
  Pass
  {
   Name "Plain"
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ MC_A MC_B
   #pragma multi_compile_local _ LC_A LC_B
   #pragma shader_feature _ SF_A SF_B
   #pragma shader_feature_local _ LF_A LF_B
   #pragma dynamic_branch DB_A DB_B
   float4 vert(float4 p : POSITION) : SV_POSITION { return p; }
   float4 frag() : SV_Target { return float4(1, 1, 1, 1); }
   ENDCG
  }
  Pass
  {
   Name "Forward"
   Tags { "LightMode" = "ForwardBase" }
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ FORWARD_A FORWARD_B
   float4 vert(float4 p : POSITION) : SV_POSITION { return p; }
   float4 frag() : SV_Target { return float4(1, 1, 1, 1); }
   ENDCG
  }
  Pass
  {
   Name "SecondPlain"
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ SECOND_A SECOND_B
   #pragma multi_compile_fog
   float4 vert(float4 p : POSITION) : SV_POSITION { return p; }
   float4 frag() : SV_Target { return float4(1, 1, 1, 1); }
   ENDCG
  }
 }
}
