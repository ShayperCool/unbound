// Writes the built-in shadow variables a ForwardAdd pass sees for a shadowed spot or point light
// into a float render target, one vector per pixel along x (the base pass writes zeros first).
Shader "Probe/GlobalsDumpAdd"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 v : POSITION) : SV_POSITION { return float4(v.xy * 2.0, 0.5, 1.0); }
            float4 frag() : SV_Target { return 0; }
            ENDCG
        }
        Pass
        {
            Tags { "LightMode" = "ForwardAdd" }
            ZTest Always ZWrite Off Cull Off Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdadd_fullshadows
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return float4(v.xy * 2.0, 0.5, 1.0); }
            float4 frag(float4 pos : SV_POSITION) : SV_Target
            {
                int i = (int)pos.x;
                if (i < 4) { float4x4 m = unity_WorldToShadow[0]; return m[i % 4]; }
                if (i == 4) return _LightShadowData;
                if (i == 5) return unity_LightShadowBias;
                if (i == 6) return _LightPositionRange;
                if (i == 7) return _LightProjectionParams;
            #if defined(SPOT) && defined(SHADOWS_DEPTH) && defined(SHADOWS_SOFT)
                if (i < 12) return _ShadowOffsets[i - 8];
                if (i == 12) return _ShadowMapTexture_TexelSize;
            #endif
                if (i == 13)
                {
                    float4 flags = 0;
                #if defined(SHADOWS_DEPTH)
                    flags.x = 1;
                #endif
                #if defined(SHADOWS_CUBE)
                    flags.y = 1;
                #endif
                #if defined(SHADOWS_SOFT)
                    flags.z = 1;
                #endif
                #if defined(SPOT)
                    flags.w = 1;
                #elif defined(POINT)
                    flags.w = 2;
                #endif
                    return flags;
                }
                if (i == 14) return unity_ShadowFadeCenterAndType;
                if (i >= 15 && i < 19) { float4x4 l = unity_WorldToLight; return l[(i - 15) % 4]; }
                return float4(-1, -1, -1, -1);
            }
            ENDCG
        }
    }
}
