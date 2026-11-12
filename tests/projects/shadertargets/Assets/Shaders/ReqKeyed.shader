Shader "Probe/Targets/ReqKeyed"
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
            #pragma require cubearray : KW_A

            #include "TargetsCommon.hlsl"
            ENDHLSL
        }
    }
}
