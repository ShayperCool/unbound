// SubShaders with different LODs and pass counts: L0 LOD 400 3 passes, L1 LOD 300 1 pass,
// L2 LOD 200 2 passes; the Fallback adds F0 (LOD 150, 4 passes) and F1 (LOD 50, 5 passes).
// Pass 0 writes the SubShader code (red, blue) and one green step; every further pass adds one
// green step, so the rendered green counts the passes drawn.
Shader "Probe/PassLod"
{
    SubShader
    {
        Tags { "SubId"="L0" }
        LOD 400
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "L0P0"
            Tags { "PassId"="L0.0" }
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return float4(0.125, 0.125, 0, 1); }
            ENDCG
        }
        Pass
        {
            Name "L0P1"
            Tags { "PassId"="L0.1" }
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return float4(0, 0.125, 0, 0); }
            ENDCG
        }
        Pass
        {
            Name "L0P2"
            Tags { "PassId"="L0.2" }
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return float4(0, 0.125, 0, 0); }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "SubId"="L1" }
        LOD 300
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "L1P0"
            Tags { "PassId"="L1.0" }
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return float4(0.25, 0.125, 0, 1); }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "SubId"="L2" }
        LOD 200
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "L2P0"
            Tags { "PassId"="L2.0" }
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return float4(0.375, 0.125, 0, 1); }
            ENDCG
        }
        Pass
        {
            Name "L2P1"
            Tags { "PassId"="L2.1" }
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return float4(0, 0.125, 0, 0); }
            ENDCG
        }
    }
    Fallback "Probe/PassFallback"
}
