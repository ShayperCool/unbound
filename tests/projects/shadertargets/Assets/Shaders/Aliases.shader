Shader "Probe/Targets/Aliases"
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
            #pragma target es3.0
            #pragma target gl4.1 KW_A
            #pragma target es3.1 KW_B

            #include "TargetsCommon.hlsl"
            ENDHLSL
        }
    }
}
