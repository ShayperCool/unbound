// A transparent surface that reads what is behind it as water graphs do: the camera's opaque
// texture a little displaced (Scene Color), and how far behind it the opaque surface lies (Scene
// Depth). _Mode 0: the scene color, tinted; 1: white fading to the water's colour with the depth
// behind; 2: the scene color fading to the water's colour.
Shader "Probe/SceneColorWater"
{
    Properties
    {
        _Mode ("Mode", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float _Mode;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screen : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float viewDepth : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                Varyings output;
                output.positionCS = position.positionCS;
                output.screen = ComputeScreenPos(position.positionCS);
                output.uv = input.uv;
                output.viewDepth = -position.positionVS.z;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.screen.xy / input.screen.w;
                float2 ripple = float2(sin(input.uv.y * 40.0), cos(input.uv.x * 40.0)) * 0.01;
                half3 scene = SampleSceneColor(uv + ripple);
                float behind = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams) - input.viewDepth;
                float fade = saturate(behind * 0.5);
                half3 water = half3(0.1, 0.3, 0.6);
                half3 color = _Mode < 0.5 ? scene * half3(0.8, 0.95, 1.0)
                    : _Mode < 1.5 ? lerp(half3(1, 1, 1), water, fade)
                    : lerp(scene, water, fade);
                return half4(color, 0.9);
            }
            ENDHLSL
        }
    }
}
