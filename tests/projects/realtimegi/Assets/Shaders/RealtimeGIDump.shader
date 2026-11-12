// Writes a renderer's realtime lightmap texels into a render target the size of the realtime
// lightmap atlas: each vertex goes to its realtime lightmap position (TEXCOORD2 with
// unity_DynamicLightmapST, as the Standard shader reads it), and each covered pixel writes the
// texel at its own center, unfiltered. _Channel 1 writes the directionality texture instead;
// _Channel 2 writes the realtime lightmap's size (width, height) for the probe to size its target.
// _Channel 3 writes the realtime lightmap's texels into a target the size of the renderer's own
// realtime lightmap UV space instead: each vertex goes to TEXCOORD2 itself, and each pixel writes
// the atlas texel its place stands for.
// Uncovered pixels keep the clear color (alpha 0); covered ones have alpha 1 from the texel's
// value or the directionality's alpha.
Shader "Hidden/RealtimeGIDump"
{
    Properties { _Channel ("Channel", Float) = 0 }
    CGINCLUDE
    #pragma vertex vert
    #pragma fragment frag
    #pragma target 4.5
    #include "UnityCG.cginc"

    float _Channel;

    struct appdata { float4 vertex : POSITION; float2 uv2 : TEXCOORD2; };
    struct v2f { float4 pos : SV_POSITION; float2 lm : TEXCOORD0; };

    v2f vert(appdata v)
    {
        v2f o;
        float2 uv = v.uv2 * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
        o.lm = uv;
        o.pos = float4((_Channel > 2.5 ? v.uv2 : uv) * 2 - 1, 0.5, 1);
        if (_ProjectionParams.x < 0) o.pos.y = -o.pos.y;
        return o;
    }

    float4 frag(v2f i) : SV_Target
    {
#if defined(DYNAMICLIGHTMAP_ON)
        float width, height;
        unity_DynamicLightmap.GetDimensions(width, height);
        float2 uv = _Channel > 2.5 ? i.lm : i.pos.xy / float2(width, height);
        if (_Channel > 2.5)
            return float4(UNITY_SAMPLE_TEX2D_LOD(unity_DynamicLightmap, uv, 0).rgb, 1);
        if (_Channel > 1.5)
            return float4(width, height, 0, 1);
        if (_Channel > 0.5)
            return UNITY_SAMPLE_TEX2D_SAMPLER_LOD(unity_DynamicDirectionality, unity_DynamicLightmap, uv, 0);
        return float4(UNITY_SAMPLE_TEX2D_LOD(unity_DynamicLightmap, uv, 0).rgb, 1);
#else
        return float4(-1, -1, -1, 1);
#endif
    }
    ENDCG

    // The Universal Render Pipeline's (a subshader of its own: a pass of the built-in pipeline
    // beside it would be drawn again by the pipeline's check for unsupported shaders).
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            ENDCG
        }
    }
    // The built-in pipeline's.
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma multi_compile_fwdbase
            ENDCG
        }
    }
}
