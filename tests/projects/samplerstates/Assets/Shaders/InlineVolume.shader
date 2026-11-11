Shader "Probe/SamplerStates/InlineVolume"
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
            Texture3D _Deep; SamplerState sampler_PointClamp; SamplerState sampler_PointRepeat; SamplerState sampler_LinearClamp; SamplerState sampler_PointClampU_ClampV_RepeatW;
            static const float2 P = float2(0.4, 0.5);
            static const float3 D = float3(1, 0.2, 0);
            #pragma vertex vert_img
            float4 frag(v2f_img i) : SV_Target { return float4(_Deep.Sample(sampler_PointClamp, float3(0.5, 0.5, 1.1)).r, _Deep.Sample(sampler_PointRepeat, float3(0.5, 0.5, 1.1)).r, _Deep.Sample(sampler_LinearClamp, float3(0.5, 0.5, 0.9)).r, _Deep.Sample(sampler_PointClampU_ClampV_RepeatW, float3(0.5, 0.5, 1.1)).r); }
            ENDCG
        }
    }
}
