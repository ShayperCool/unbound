Shader "Probe/SamplerStates/Shapes"
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
            TextureCube _Cube; SamplerState sampler_Cube; Texture3D _Vol; SamplerState sampler_Vol; Texture2D _B; SamplerState sampler_B;
            static const float2 P = float2(0.4, 0.5);
            static const float3 D = float3(1, 0.2, 0);
            #pragma vertex vert_img
            float4 frag(v2f_img i) : SV_Target { return float4(_Cube.Sample(sampler_Cube, D).r, _Cube.Sample(sampler_B, D).r, _Vol.Sample(sampler_Vol, float3(P, 0.5)).r, _Vol.Sample(sampler_B, float3(P, 0.5)).r); }
            ENDCG
        }
    }
}
