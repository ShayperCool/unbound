Shader "Probe/SamplerStates/InlineAxes"
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
            Texture2D _B; SamplerState sampler_LinearClampU_RepeatV; SamplerState sampler_LinearRepeatU_ClampV; SamplerState sampler_LinearMirror; SamplerState sampler_LinearRepeat_ClampU;
            static const float2 P = float2(0.4, 0.5);
            static const float3 D = float3(1, 0.2, 0);
            #pragma vertex vert_img
            float4 frag(v2f_img i) : SV_Target { return float4(_B.Sample(sampler_LinearClampU_RepeatV, float2(0.9, 0.5)).r, _B.Sample(sampler_LinearRepeatU_ClampV, float2(0.9, 0.5)).r, _B.Sample(sampler_LinearMirror, float2(1.9, 0.5)).r, _B.Sample(sampler_LinearRepeat_ClampU, float2(0.9, 0.5)).r); }
            ENDCG
        }
    }
}
