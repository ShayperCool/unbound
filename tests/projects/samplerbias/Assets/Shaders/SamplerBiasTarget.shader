Shader "Probe/SamplerBiasTarget"
{
    Properties { _MainTex ("Legacy texture", 2D) = "white" {} _ArrayTex ("Array texture", 2DArray) = "" {} _Level ("Level", Float) = 0 _Mode ("Operation", Float) = 0 _Texel ("Texel size", Vector) = (0.0625, 0.125, 0, 0) }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            Texture2D<float4> _ModernTex;
            SamplerState sampler_ModernTex;
            UNITY_DECLARE_TEX2DARRAY(_ArrayTex);
            float _Level, _Mode;
            float4 _Texel;
            float4 frag(v2f_img i) : SV_Target
            {
                // An isotropic footprint of 2^_Level texels, and one eight times as long across.
                float2 dx = float2(exp2(_Level) * _Texel.x, 0), dy = float2(0, exp2(_Level) * _Texel.y);
                float2 wide = dx * 8;
                if (_Mode < 0.5) return tex2Dgrad(_MainTex, i.uv, dx, dy);
                if (_Mode < 1.5) return tex2D(_MainTex, i.uv);
                if (_Mode < 2.5) return tex2Dgrad(_MainTex, i.uv, wide, dy);
                if (_Mode < 3.5) return _ModernTex.SampleGrad(sampler_ModernTex, i.uv, dx, dy);
                if (_Mode < 4.5) return _ModernTex.Sample(sampler_ModernTex, i.uv);
                if (_Mode < 5.5) return _ModernTex.SampleGrad(sampler_ModernTex, i.uv, wide, dy);
                if (_Mode < 6.5) return _ArrayTex.SampleGrad(sampler_ArrayTex, float3(i.uv, 0), dx, dy);
                if (_Mode < 7.5) return _ArrayTex.Sample(sampler_ArrayTex, float3(i.uv, 0));
                return _ArrayTex.SampleGrad(sampler_ArrayTex, float3(i.uv, 0), wide, dy);
            }
            ENDCG
        }
    }
}
