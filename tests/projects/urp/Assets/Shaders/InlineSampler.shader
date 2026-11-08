// Samples a Point/Repeat texture through an inline linear clamp sampler (left half) and a
// Bilinear/Clamp texture through an inline point mirror sampler (right half), outside their
// 0..1 range (the URP probe's inline_sampler scenario: on OpenGL Core each texture keeps its own
// sampler state).
Shader "Probe/InlineSampler"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            Texture2D _PointRepeatTex;
            Texture2D _LinearClampTex;
            SamplerState probe_linear_clamp_sampler;
            SamplerState probe_point_mirror_sampler;

            struct Attributes { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes a)
            {
                Varyings o;
                o.position = TransformObjectToHClip(a.position.xyz);
                o.uv = a.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * float2(5.0, 2.5) - 0.75;
                half4 left = _PointRepeatTex.Sample(probe_linear_clamp_sampler, p);
                half4 right = _LinearClampTex.Sample(probe_point_mirror_sampler, p - float2(2.5, 0.0));
                return i.uv.x < 0.5 ? left : right;
            }
            ENDHLSL
        }
    }
}
