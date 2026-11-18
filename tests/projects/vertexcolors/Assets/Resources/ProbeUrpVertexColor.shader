// URP's own include (which declares unity_SpriteColor among the per-draw values) and the
// interpolated vertex color written unchanged: unity_SpriteColor is declared, not read.
Shader "Probe/UrpVertexColor"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                return output;
            }
            float4 frag(Varyings input) : SV_Target { return input.color; }
            ENDHLSL
        }
    }
}
