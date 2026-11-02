// A first SubShader for another render pipeline (T0, RenderPipeline UniversalPipeline, 3 passes)
// before an untagged one (T1, 2 passes); neither has a LOD; no Fallback.
Shader "Probe/PassTagged"
{
    SubShader
    {
        Tags { "SubId"="T0" "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "T0P0"
            Tags { "PassId"="T0.0" }
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return float4(0.75, 0.125, 0, 1); }
            ENDCG
        }
        Pass
        {
            Name "T0P1"
            Tags { "PassId"="T0.1" }
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
            Name "T0P2"
            Tags { "PassId"="T0.2" }
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
        Tags { "SubId"="T1" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "T1P0"
            Tags { "PassId"="T1.0" }
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return float4(0.875, 0.125, 0, 1); }
            ENDCG
        }
        Pass
        {
            Name "T1P1"
            Tags { "PassId"="T1.1" }
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
