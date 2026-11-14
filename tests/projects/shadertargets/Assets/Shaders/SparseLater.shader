Shader "Probe/Targets/SparseLater"
{
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ KW_A
            #pragma multi_compile_local _ KW_B

            #pragma target 2.0
            #pragma require sparsetex : KW_A
            #include "TargetsCommon.hlsl"
            ENDHLSL
        }
    }
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
