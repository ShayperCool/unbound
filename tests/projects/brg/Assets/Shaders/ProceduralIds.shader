// An unlit URP shader that reads no vertex attributes, for the BatchRendererGroup probe's
// procedural_graphics scenario (Graphics.RenderPrimitives / DrawProcedural, no batch): vertex v
// lies at corner v % 4 of a unit square (counter-clockwise from the origin) shifted by 1.3 units
// along x for each further group of four (v / 4), as Probe/DotsProcedural's, scaled by _Scale and
// placed at _Offset in world space; with _Expand set, each six vertex ids are one such square's
// two triangles.
Shader "Probe/ProceduralIds"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Offset ("World offset", Vector) = (0, 0, 0, 0)
        _Scale ("Scale", Float) = 1
        _Expand ("Expand triangles", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ProceduralForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _Offset;
                float _Scale;
                float _Expand;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                uint v = input.vertexID;
                if (_Expand > 0.5)
                {
                    uint k = v % 6;
                    v = (v / 6) * 4 + (k < 3 ? k : (k == 3 ? 0 : k - 2));
                }
                uint corner = v % 4;
                float x = (corner == 1 || corner == 2) ? 1.0 : 0.0;
                float y = corner >= 2 ? 1.0 : 0.0;
                float3 position = float3(x + 1.3 * (v / 4), y, 0.0) * _Scale + _Offset.xyz;
                Varyings output;
                output.positionCS = TransformWorldToHClip(position);
                return output;
            }

            half4 frag(Varyings input) : SV_Target { return _BaseColor; }
            ENDHLSL
        }
    }
}
