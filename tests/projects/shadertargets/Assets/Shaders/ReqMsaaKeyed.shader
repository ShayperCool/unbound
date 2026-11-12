Shader "Probe/Targets/ReqMsaaKeyed"
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
            #pragma require msaatex : KW_B

            #include "TargetsCommon.hlsl"
            ENDHLSL
        }
    }
}
