// An unlit URP shader with DOTS instancing that reads no vertex attributes: the BatchRendererGroup
// probe's procedural commands draw it. Vertex v is corner v % 4 of a unit square (counter-clockwise
// from the origin) shifted by 1.3 units along x for each further group of four (v / 4), so the
// vertex ids a draw reads show where its shape lies. With _Expand set, each six vertex ids are
// the two triangles of one such square (ids 0-5 the first square's corners 0, 1, 2, 0, 2, 3).
// The instance's unity_ObjectToWorld and _BaseColor come from its batch. With _Show 1 or 2 the
// colour is instead the per-draw header's mesh bounds centre or extent (0.5 + value / 4 per axis).
Shader "Probe/DotsProcedural"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Expand ("Expand triangles", Float) = 0
        _Show ("Show the header's bounds centre (1) or extent (2)", Float) = 0
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
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Expand;
                float _Show;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _BaseColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor)
            #endif

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation float4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                uint v = input.vertexID;
                if (_Expand > 0.5)
                {
                    uint k = v % 6;
                    v = (v / 6) * 4 + (k < 3 ? k : (k == 3 ? 0 : k - 2));
                }
                uint corner = v % 4;
                float x = (corner == 1 || corner == 2) ? 1.0 : 0.0;
                float y = corner >= 2 ? 1.0 : 0.0;
                float3 position = float3(x + 1.3 * (v / 4), y, 0.0);
                Varyings output;
                output.positionCS = TransformObjectToHClip(position);
                output.color = _BaseColor;
                #ifdef UNITY_DOTS_INSTANCING_ENABLED
                if (_Show > 0.5)
                {
                    float3 bound = _Show < 1.5 ? LoadDOTSInstancedData_MeshLocalBoundCenter() : LoadDOTSInstancedData_MeshLocalBoundExtent();
                    output.color = float4(saturate(0.5 + 0.25 * bound), 1.0);
                }
                #endif
                return output;
            }

            half4 frag(Varyings input) : SV_Target { return input.color; }
            ENDHLSL
        }
    }
}
