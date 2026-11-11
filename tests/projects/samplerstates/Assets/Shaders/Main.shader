Shader "Probe/SamplerStates/Main"
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
            Texture2D _MainTex; SamplerState sampler_MainTex; Texture2D _B; SamplerState sampler_B;
            static const float2 P = float2(0.4, 0.5);
            static const float3 D = float3(1, 0.2, 0);
            #pragma vertex vert_img
            float4 frag(v2f_img i) : SV_Target { return float4(_MainTex.Sample(sampler_MainTex, P).r, _MainTex.Sample(sampler_B, P).r, _B.Sample(sampler_B, P).r, 1); }
            ENDCG
        }
    }
}
