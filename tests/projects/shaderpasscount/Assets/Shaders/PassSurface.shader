// A surface SubShader first (S0, LOD 300: Unity generates its passes; emission (0.125, 0, 0.5),
// no albedo) and a vertex/fragment one (S1, LOD 200, 3 passes, code (0.25, 0.5)); no Fallback.
Shader "Probe/PassSurface"
{
    SubShader
    {
        Tags { "SubId"="S0" "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Lambert
        struct Input { float4 color : COLOR; };
        void surf(Input IN, inout SurfaceOutput o) { o.Albedo = 0; o.Emission = float3(0.125, 0, 0.5); o.Alpha = 1; }
        ENDCG
    }
    SubShader
    {
        Tags { "SubId"="S1" }
        LOD 200
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "S1P0"
            Tags { "PassId"="S1.0" }
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return float4(0.25, 0.125, 0.5, 1); }
            ENDCG
        }
        Pass
        {
            Name "S1P1"
            Tags { "PassId"="S1.1" }
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
            Name "S1P2"
            Tags { "PassId"="S1.2" }
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
