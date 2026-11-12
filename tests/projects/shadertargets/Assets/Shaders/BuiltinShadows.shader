Shader "Probe/Targets/BuiltinShadows"
{
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ SHADOWS_SOFT
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma target 2.0

            #include "TargetsCommon.hlsl"
            ENDHLSL
        }
    }
}
