Shader "Probe/SamplerBias"
{
    Properties { _MainTex ("Legacy texture", 2D) = "white" {} _ModernTex ("Object texture", 2D) = "white" {} _Level ("Level", Float) = 0 _Mode ("Operation", Float) = 0 }
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
            float _Level, _Mode;
            float4 _MainTex_TexelSize;
            float4 frag(v2f_img i) : SV_Target
            {
                float2 dx = float2(exp2(_Level) * _MainTex_TexelSize.x, 0), dy = float2(0, exp2(_Level) * _MainTex_TexelSize.y);
                if (_Mode < 0.5) return tex2Dlod(_MainTex, float4(i.uv, 0, _Level));
                if (_Mode < 1.5) return tex2Dbias(_MainTex, float4(i.uv, 0, _Level));
                if (_Mode < 2.5) return tex2Dgrad(_MainTex, i.uv, dx, dy);
                if (_Mode < 3.5) return tex2D(_MainTex, i.uv);
                if (_Mode < 4.5) return _ModernTex.SampleLevel(sampler_ModernTex, i.uv, _Level);
                if (_Mode < 5.5) return _ModernTex.SampleGrad(sampler_ModernTex, i.uv, dx, dy);
                return _ModernTex.Sample(sampler_ModernTex, i.uv);
            }
            ENDCG
        }
    }
}
