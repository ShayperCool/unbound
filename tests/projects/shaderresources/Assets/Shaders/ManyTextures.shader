// 24 textures sampled with one sampler state in one stage (URP's Lit with Adaptive Probe Volumes
// binds 21 textures and shares sampler states among them): the last, the 17th and the mean of all.
Shader "Probe/ManyTextures"
{
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            Texture2D _T0;
            Texture2D _T1;
            Texture2D _T2;
            Texture2D _T3;
            Texture2D _T4;
            Texture2D _T5;
            Texture2D _T6;
            Texture2D _T7;
            Texture2D _T8;
            Texture2D _T9;
            Texture2D _T10;
            Texture2D _T11;
            Texture2D _T12;
            Texture2D _T13;
            Texture2D _T14;
            Texture2D _T15;
            Texture2D _T16;
            Texture2D _T17;
            Texture2D _T18;
            Texture2D _T19;
            Texture2D _T20;
            Texture2D _T21;
            Texture2D _T22;
            Texture2D _T23;
            SamplerState sampler_point_clamp;

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = float2(0.5, 0.5);
                float all = _T0.SampleLevel(sampler_point_clamp, uv, 0).r + _T1.SampleLevel(sampler_point_clamp, uv, 0).r + _T2.SampleLevel(sampler_point_clamp, uv, 0).r + _T3.SampleLevel(sampler_point_clamp, uv, 0).r + _T4.SampleLevel(sampler_point_clamp, uv, 0).r + _T5.SampleLevel(sampler_point_clamp, uv, 0).r + _T6.SampleLevel(sampler_point_clamp, uv, 0).r + _T7.SampleLevel(sampler_point_clamp, uv, 0).r + _T8.SampleLevel(sampler_point_clamp, uv, 0).r + _T9.SampleLevel(sampler_point_clamp, uv, 0).r + _T10.SampleLevel(sampler_point_clamp, uv, 0).r + _T11.SampleLevel(sampler_point_clamp, uv, 0).r + _T12.SampleLevel(sampler_point_clamp, uv, 0).r + _T13.SampleLevel(sampler_point_clamp, uv, 0).r + _T14.SampleLevel(sampler_point_clamp, uv, 0).r + _T15.SampleLevel(sampler_point_clamp, uv, 0).r + _T16.SampleLevel(sampler_point_clamp, uv, 0).r + _T17.SampleLevel(sampler_point_clamp, uv, 0).r + _T18.SampleLevel(sampler_point_clamp, uv, 0).r + _T19.SampleLevel(sampler_point_clamp, uv, 0).r + _T20.SampleLevel(sampler_point_clamp, uv, 0).r + _T21.SampleLevel(sampler_point_clamp, uv, 0).r + _T22.SampleLevel(sampler_point_clamp, uv, 0).r + _T23.SampleLevel(sampler_point_clamp, uv, 0).r;
                return float4(_T23.SampleLevel(sampler_point_clamp, uv, 0).r, _T16.SampleLevel(sampler_point_clamp, uv, 0).r, all / 24.0, 1);
            }
            ENDCG
        }
    }
}
