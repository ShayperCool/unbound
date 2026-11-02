// An HLSLPROGRAM pass without Unity's includes.
Shader "Probe/KwHlsl"
{
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ HLSL_A
            #pragma shader_feature_local HLSL_LOCAL
            float4 vert(float4 v : POSITION) : SV_POSITION { return v; }
            float4 frag() : SV_Target { return 1; }
            ENDHLSL
        }
    }
}
