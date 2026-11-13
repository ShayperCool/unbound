// A struct holding a structured buffer and a texture, filled from globals (as URP's Adaptive Probe
// Volume resources are), read through a function, through a copy, and directly.
Shader "Probe/BufferMember"
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

            StructuredBuffer<float4> _Colors;
            Texture2D _Tint;
            SamplerState sampler_Tint;
            int _Index;
            int _Mode;

            struct Resources
            {
                StructuredBuffer<float4> colors;
                Texture2D tint;
            };

            Resources Fill()
            {
                Resources r;
                r.colors = _Colors;
                r.tint = _Tint;
                return r;
            }

            float4 Read(Resources r, int i)
            {
                return r.colors[i] * r.tint.SampleLevel(sampler_Tint, float2(0.5, 0.5), 0);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                Resources r = Fill();
                if (_Mode == 1)
                {
                    Resources copy = r;
                    return Read(copy, _Index + 1);
                }
                if (_Mode == 2)
                    return r.colors[_Index];
                return Read(r, _Index);
            }
            ENDCG
        }
    }
}
