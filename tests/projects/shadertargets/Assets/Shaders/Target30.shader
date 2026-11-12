Shader "Probe/Targets/Target30"
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
            #pragma target 3.0

            #include "TargetsCommon.hlsl"
            ENDHLSL
        }
    }
}
