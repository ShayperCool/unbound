// The main light URP gives an opaque pass: pixel x 0 holds _MainLightColor, pixel x 1
// _MainLightPosition.
Shader "Hidden/UrpSunMainLight"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 vert(float4 vertex : POSITION) : SV_POSITION { return TransformObjectToHClip(vertex.xyz); }

            float4 frag(float4 position : SV_POSITION) : SV_Target
            {
                return position.x < 1 ? float4(_MainLightColor.rgb, 1) : _MainLightPosition;
            }
            ENDHLSL
        }
    }
}
