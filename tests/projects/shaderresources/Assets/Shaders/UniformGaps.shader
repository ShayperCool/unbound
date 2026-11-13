// A material block where one member between those the fragment program reads is read by the vertex program
// alone (as Shader Graph materials' texel sizes and tiling often are): both programs share one
// uniform layout, so the fragment program's block has a gap of a whole float4 row there.
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
