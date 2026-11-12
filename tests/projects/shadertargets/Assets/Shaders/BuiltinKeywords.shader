Shader "Probe/Targets/BuiltinKeywords"
{
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ SHADOWS_SCREEN
            #pragma target 2.0

            #include "TargetsCommon.hlsl"
            ENDHLSL
        }
    }
}
