Shader "Probe/Targets/KeyedOnly"
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
            #pragma target 4.0 KW_A
            #include "TargetsCommon.hlsl"
            ENDHLSL
        }
    }
}
