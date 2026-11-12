Shader "Probe/Targets/BuiltinCompute"
{
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ PROCEDURAL_INSTANCING_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON

            #include "TargetsCommon.hlsl"
            ENDHLSL
        }
    }
}
