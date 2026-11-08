// The URP probe's subpass_mrt scenarios: pass 0 writes two colour outputs over one rectangle
// (orange into the first, a uv gradient into the second); pass 1 shows a target, loaded texel by
// texel, in the clip-space rectangle _MrtRect (min xy, max xy) of the camera colour.
Shader "Probe/Mrt"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

        struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

        // Two triangles over the clip-space rectangle min..max.
        Varyings Rect(uint id, float2 min, float2 max)
        {
            float2 corner = float2(id == 1 || id == 2 || id == 4 ? 1.0 : 0.0, id == 2 || id == 4 || id == 5 ? 1.0 : 0.0);
            Varyings o;
            o.position = float4(lerp(min, max, corner), 0.5, 1.0);
            o.uv = corner;
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "Write"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Outputs { half4 color : SV_Target0; half4 second : SV_Target1; };

            Varyings vert(uint id : SV_VertexID) { return Rect(id, float2(-0.9, 0.2), float2(-0.4, 0.8)); }

            Outputs frag(Varyings i)
            {
                Outputs o;
                o.color = half4(1.0, 0.5, 0.0, 1.0);
                o.second = half4(i.uv, 0.8, 1.0);
                return o;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Show"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            TEXTURE2D(_MrtSecond);
            float4 _MrtSecond_TexelSize;
            float4 _MrtRect;

            Varyings vert(uint id : SV_VertexID) { return Rect(id, _MrtRect.xy, _MrtRect.zw); }

            half4 frag(Varyings i) : SV_Target
            {
                return LOAD_TEXTURE2D(_MrtSecond, min(uint2(i.uv * _MrtSecond_TexelSize.zw), uint2(_MrtSecond_TexelSize.zw) - 1));
            }
            ENDHLSL
        }
    }
}
