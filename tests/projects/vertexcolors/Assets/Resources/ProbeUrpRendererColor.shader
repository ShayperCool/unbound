// URP's own include (which declares unity_SpriteColor) and _RendererColor, a global of the
// shader's own, written unchanged: what the renderer sets there.
Shader "Probe/UrpRendererColor"
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
            float4 _RendererColor;
            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            float4 frag() : SV_Target { return _RendererColor; }
            ENDHLSL
        }
    }
}
