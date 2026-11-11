Shader "Probe/SamplerStates/CmpInline"
{
    Properties { _MainTex ("Main", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            Texture2D _Shadow; SamplerComparisonState sampler_Shadow; SamplerState sampler_PointClamp; Texture2D _B; SamplerState sampler_B;
            static const float2 P = float2(0.4, 0.5);
            static const float3 D = float3(1, 0.2, 0);
            #pragma vertex vert_img
            float4 frag(v2f_img i) : SV_Target { return float4(_Shadow.SampleCmpLevelZero(sampler_Shadow, P, 0.5), _Shadow.Sample(sampler_PointClamp, P).r, _B.Sample(sampler_B, P).r, 1); }
            ENDCG
        }
    }
}
