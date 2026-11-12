Shader "Probe/Targets/Target46Cube"
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
            #pragma target 4.6
            #pragma require cubearray

            #include "TargetsCommon.hlsl"
            ENDHLSL
        }
    }
}
