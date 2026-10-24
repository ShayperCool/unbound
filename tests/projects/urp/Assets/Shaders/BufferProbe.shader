// Reads per-vertex offsets from a StructuredBuffer and colors from a ByteAddressBuffer by vertex id
// (the URP probe's structured_buffer scenario).
Shader "Probe/BufferProbe"
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
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float4> _Offsets;
            ByteAddressBuffer _Colors;

            struct Attributes { float4 position : POSITION; uint id : SV_VertexID; };
            struct Varyings { float4 position : SV_POSITION; float4 color : COLOR; };

            Varyings vert(Attributes a)
            {
                Varyings o;
                o.position = TransformObjectToHClip(a.position.xyz + _Offsets[a.id].xyz);
                o.color = asfloat(_Colors.Load4(a.id * 16));
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return i.color; }
            ENDHLSL
        }
    }
}
