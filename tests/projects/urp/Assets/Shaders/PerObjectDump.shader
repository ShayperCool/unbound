// The per-object constants URP's forward path reads, one per pixel of a 20x1 float target:
// unity_SpecCube0/1 BoxMin, BoxMax, ProbePosition, HDR, each cubemap sampled in one direction,
// unity_LightData, unity_LightmapST, unity_ProbesOcclusion and unity_SHAr .. unity_SHC. The
// mesh's UVs place it over the whole target.
Shader "Probe/PerObjectDump"
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
                float3 direction = normalize(float3(1.0, 0.3, 0.2));
                if (i == 0) return unity_SpecCube0_BoxMin;
                if (i == 1) return unity_SpecCube0_BoxMax;
                if (i == 2) return unity_SpecCube0_ProbePosition;
                if (i == 3) return unity_SpecCube0_HDR;
                if (i == 4) return unity_SpecCube1_BoxMin;
                if (i == 5) return unity_SpecCube1_BoxMax;
                if (i == 6) return unity_SpecCube1_ProbePosition;
                if (i == 7) return unity_SpecCube1_HDR;
                if (i == 8) return SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, direction, 0);
                if (i == 9) return SAMPLE_TEXTURECUBE_LOD(unity_SpecCube1, samplerunity_SpecCube1, direction, 0);
                if (i == 10) return unity_LightData;
                if (i == 11) return unity_LightmapST;
                if (i == 12) return unity_ProbesOcclusion;
                if (i == 13) return unity_SHAr;
                if (i == 14) return unity_SHAg;
                if (i == 15) return unity_SHAb;
                if (i == 16) return unity_SHBr;
                if (i == 17) return unity_SHBg;
                if (i == 18) return unity_SHBb;
                return unity_SHC;
            }
            ENDHLSL
        }
    }
}
