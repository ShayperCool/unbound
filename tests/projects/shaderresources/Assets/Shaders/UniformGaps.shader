// A layout control: a material block whose members around one the vertex program alone reads are
// read by the fragment program. Both programs keep every member of the block, so no gap arises;
// it checks the uniform layout, not the type order fix (README.md).
Shader "Probe/UniformGaps"
{
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            CBUFFER_START(UnityPerMaterial)
            float _Scale;
            float4 _Used;
            float _Depth;
            float2 _Tiling;
            float4 _VertexOnly;
            float4 _Bias;
            CBUFFER_END

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord + _VertexOnly.xy;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return _Used * _Scale + _Bias + float4(i.uv * 0.001 * _Tiling, _Depth, 0);
            }
            ENDCG
        }
    }
}
