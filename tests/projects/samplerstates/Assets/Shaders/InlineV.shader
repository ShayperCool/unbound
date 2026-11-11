Shader "Probe/SamplerStates/InlineV"
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
            Texture2D _Q; SamplerState sampler_LinearClampU_RepeatV; SamplerState sampler_LinearClampUV; SamplerState sampler_LinearMirrorOnceU;
            static const float2 P = float2(0.4, 0.5);
            static const float3 D = float3(1, 0.2, 0);
            #pragma vertex vert_img
            float4 frag(v2f_img i) : SV_Target { return float4(_Q.Sample(sampler_LinearClampU_RepeatV, float2(0.5, 0.9)).r, _Q.Sample(sampler_LinearClampU_RepeatV, float2(0.9, 0.5)).r, _Q.Sample(sampler_LinearClampUV, float2(0.5, 0.9)).r, _Q.Sample(sampler_LinearMirrorOnceU, float2(0.5, 1.9)).r); }
            ENDCG
        }
    }
}
