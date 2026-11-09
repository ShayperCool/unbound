Shader "Probe/WarmupFresh"
{
 SubShader
 {
  LOD 300
  Pass
  {
   Cull Off ZWrite Off ZTest Always
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.5
   #pragma multi_compile _ WARM_RED
   #pragma shader_feature_local _ WARM_BLUE
   #pragma multi_compile_instancing
   #include "UnityCG.cginc"
   struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
   float4 vert(appdata v) : SV_POSITION
   {
    UNITY_SETUP_INSTANCE_ID(v);
    return UnityObjectToClipPos(v.vertex);
   }
   float4 frag() : SV_Target
   {
    float4 color = float4(0.2, 0.4, 0.8, 1);
    #ifdef WARM_RED
     color.r = 1;
    #endif
    #ifdef WARM_BLUE
     color.b = 1;
    #endif
    #ifdef INSTANCING_ON
     color.r = 0.6;
    #endif
    return color;
   }
   ENDCG
  }
 }
 SubShader
 {
  LOD 100
  Pass
  {
   Cull Off ZWrite Off ZTest Always
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.5
   #include "UnityCG.cginc"
   float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
   float4 frag() : SV_Target { return float4(.25,.375,.75,1); }
   ENDCG
  }
 }

}
