// Writes the SpeedTree 9 wind parameters the GPU Resident Drawer gives a tree instance: pixel x of
// a 22x1 target gets parameter x (DOTS_ST_WindParam0 to 10, then DOTS_ST_WindHistoryParam0 to
// 10, the instance data the drawer writes for a tree). The mesh is a quad whose UVs cover [0, 1],
// so it covers the target. Without DOTS instancing (the ordinary draw path) it writes -1.
Shader "Probe/DrawerWindDump"
{
    Properties { _Unused ("Unused", Float) = 0 }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "WindDump"
            Tags { "LightMode" = "UniversalForward" }
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Unused;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
            UNITY_DOTS_INSTANCING_START(UserPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindParam0)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindParam1)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindParam2)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindParam3)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindParam4)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindParam5)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindParam6)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindParam7)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindParam8)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindParam9)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindParam10)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindHistoryParam0)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindHistoryParam1)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindHistoryParam2)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindHistoryParam3)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindHistoryParam4)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindHistoryParam5)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindHistoryParam6)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindHistoryParam7)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindHistoryParam8)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindHistoryParam9)
                UNITY_DOTS_INSTANCED_PROP(float4, DOTS_ST_WindHistoryParam10)
            UNITY_DOTS_INSTANCING_END(UserPropertyMetadata)
            #define WIND(name) UNITY_ACCESS_DOTS_INSTANCED_PROP(float4, name)
            #else
            #define WIND(name) float4(-1, -1, -1, -1)
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = float4(input.uv * 2.0 - 1.0, 0.5, 1.0);
                // Row 0 at the bottom of the target, also where the device's render textures are
                // upside down (the projection flips them: _ProjectionParams.x is -1).
                output.positionCS.y *= _ProjectionParams.x;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                int i = (int)input.positionCS.x;
                if (i == 0) return WIND(DOTS_ST_WindParam0);
                if (i == 1) return WIND(DOTS_ST_WindParam1);
                if (i == 2) return WIND(DOTS_ST_WindParam2);
                if (i == 3) return WIND(DOTS_ST_WindParam3);
                if (i == 4) return WIND(DOTS_ST_WindParam4);
                if (i == 5) return WIND(DOTS_ST_WindParam5);
                if (i == 6) return WIND(DOTS_ST_WindParam6);
                if (i == 7) return WIND(DOTS_ST_WindParam7);
                if (i == 8) return WIND(DOTS_ST_WindParam8);
                if (i == 9) return WIND(DOTS_ST_WindParam9);
                if (i == 10) return WIND(DOTS_ST_WindParam10);
                if (i == 11) return WIND(DOTS_ST_WindHistoryParam0);
                if (i == 12) return WIND(DOTS_ST_WindHistoryParam1);
                if (i == 13) return WIND(DOTS_ST_WindHistoryParam2);
                if (i == 14) return WIND(DOTS_ST_WindHistoryParam3);
                if (i == 15) return WIND(DOTS_ST_WindHistoryParam4);
                if (i == 16) return WIND(DOTS_ST_WindHistoryParam5);
                if (i == 17) return WIND(DOTS_ST_WindHistoryParam6);
                if (i == 18) return WIND(DOTS_ST_WindHistoryParam7);
                if (i == 19) return WIND(DOTS_ST_WindHistoryParam8);
                if (i == 20) return WIND(DOTS_ST_WindHistoryParam9);
                return WIND(DOTS_ST_WindHistoryParam10);
            }
            ENDHLSL
        }
    }
}
