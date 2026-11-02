// The Fallback of Probe/PassLod: F0 LOD 150 4 passes, F1 LOD 50 5 passes, no Fallback.
Shader "Probe/PassFallback"
{
    SubShader
    {
        Tags { "SubId"="F0" }
        LOD 150
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "F0P0"
            Tags { "PassId"="F0.0" }
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return float4(0.5, 0.125, 0, 1); }
            ENDCG
        }
        Pass
        {
            Name "F0P1"
            Tags { "PassId"="F0.1" }
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
            Name "F0P2"
            Tags { "PassId"="F0.2" }
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
            Name "F0P3"
            Tags { "PassId"="F0.3" }
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
        Tags { "SubId"="F1" }
        LOD 50
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "F1P0"
            Tags { "PassId"="F1.0" }
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return float4(0.625, 0.125, 0, 1); }
            ENDCG
        }
        Pass
        {
            Name "F1P1"
            Tags { "PassId"="F1.1" }
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
            Name "F1P2"
            Tags { "PassId"="F1.2" }
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
            Name "F1P3"
            Tags { "PassId"="F1.3" }
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
            Name "F1P4"
            Tags { "PassId"="F1.4" }
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
    Fallback Off
}
