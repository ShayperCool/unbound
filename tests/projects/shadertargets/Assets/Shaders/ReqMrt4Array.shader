Shader "Probe/Targets/ReqMrt4Array"
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
            #pragma require mrt4 : KW_A
            #pragma require 2darray : KW_B

            #include "TargetsCommon.hlsl"
            ENDHLSL
        }
    }
}
