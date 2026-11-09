// The per-object lights URP's Forward path reads, one per pixel of a 9x1 float target:
// unity_LightData, then the additional light data position (_AdditionalLightsPosition) of each of
// the object's eight per-object lights in their order (GetPerObjectLightIndex), -1 past
// unity_LightData.y. The mesh's UVs place it over the whole target.
Shader "Probe/LightOrderDump"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = float4(input.uv * 2.0 - 1.0, 0.5, 1.0);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                int i = (int)input.positionCS.x;
                if (i == 0) return unity_LightData;
                uint slot = (uint)(i - 1);
                if (slot >= (uint)unity_LightData.y) return float4(-1.0, -1.0, -1.0, -1.0);
                return _AdditionalLightsPosition[GetPerObjectLightIndex(slot)];
            }
            ENDHLSL
        }
    }
}
