Shader "Probe/Targets/ReqComputeTess"
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
            #pragma require compute : KW_A
            #pragma require tessellation : KW_B

            #include "TargetsCommon.hlsl"
            ENDHLSL
        }
    }
}
