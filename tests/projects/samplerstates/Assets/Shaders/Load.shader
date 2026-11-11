Shader "Probe/SamplerStates/Load"
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
            Texture2D _A; SamplerState sampler_A; Texture2D _B; SamplerState sampler_B;
            static const float2 P = float2(0.4, 0.5);
            static const float3 D = float3(1, 0.2, 0);
            #pragma vertex vert_img
            float4 frag(v2f_img i) : SV_Target { return float4(_A.Sample(sampler_A, P).r, _A.Sample(sampler_B, P).r, _A.Load(int3(1, 0, 0)).r, _B.Sample(sampler_B, P).r); }
            ENDCG
        }
    }
}
