Shader "Probe/Targets/SparseFallbackControl"
{
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex v
            #pragma fragment f
            float4 v(float4 p : POSITION) : SV_POSITION { return float4(p.xy * 2 - 1, 0.5, 1); }
            float4 f() : SV_TARGET { return float4(0.125,0.25,0.5,1); }
            ENDHLSL
        }
    }
}
