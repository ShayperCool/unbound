Shader "Probe/Targets/KeyedFirst"
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
            #pragma target 4.5 KW_A
            #pragma target 3.0
            #include "TargetsCommon.hlsl"
            ENDHLSL
        }
    }
}
