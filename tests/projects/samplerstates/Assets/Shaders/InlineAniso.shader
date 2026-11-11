Shader "Probe/SamplerStates/InlineAniso"
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
            Texture2D _Mips; SamplerState sampler_Mips; SamplerState sampler_TrilinearClamp; SamplerState sampler_TrilinearClamp_Aniso8; SamplerState sampler_TrilinearClamp_Aniso2;
            static const float2 X = float2(0.5, 0), Y = float2(0, 0.0625), C = float2(0.5, 0.5);
            static const float2 P = float2(0.4, 0.5);
            static const float3 D = float3(1, 0.2, 0);
            #pragma vertex vert_img
            float4 frag(v2f_img i) : SV_Target { return float4(_Mips.SampleGrad(sampler_TrilinearClamp, C, X, Y).r, _Mips.SampleGrad(sampler_TrilinearClamp_Aniso8, C, X, Y).r, _Mips.SampleGrad(sampler_TrilinearClamp_Aniso2, C, X, Y).r, _Mips.SampleGrad(sampler_Mips, C, X, Y).r); }
            ENDCG
        }
    }
}
