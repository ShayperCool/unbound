Shader "Probe/WarmupFallback/Mid"
{
 SubShader
 {
  Pass
  {
   Tags { "LightMode" = "ForwardBase" }
   Cull Off ZWrite Off ZTest Always
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag

   float4 vert(float4 v : POSITION) : SV_POSITION { return v; }
   float4 frag() : SV_Target
   {
    float4 color = float4(0.2, 0.2, 0.2, 1);
    #ifdef FALLBACK_ON
     color.r = 0.8;
    #endif
    return color;
   }
   ENDCG
  }
 }
 Fallback "Probe/WarmupFallback/Base"
}
