// Writes built-in shader variables into a float render target, one vector per pixel along x, so
// the probe can read their exact values while a shadowed directional light is in the scene.
Shader "Probe/GlobalsDump"
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
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return float4(v.xy * 2.0, 0.5, 1.0); }
            float4 frag(float4 pos : SV_POSITION) : SV_Target
            {
                int i = (int)pos.x;
                if (i < 16) { float4x4 m = unity_WorldToShadow[i / 4]; return m[i % 4]; }
                if (i == 16) return _LightSplitsNear;
                if (i == 17) return _LightSplitsFar;
                if (i < 22) return unity_ShadowSplitSpheres[i - 18];
                if (i == 22) return unity_ShadowSplitSqRadii;
                if (i == 23) return _LightShadowData;
                if (i == 24) return unity_ShadowFadeCenterAndType;
                if (i == 25) return _LightPositionRange;
                if (i == 26) return unity_LightShadowBias;
                if (i == 27) return _ProjectionParams;
            #if defined(SHADOWS_SCREEN)
                if (i == 28) return float4(1, 0, 0, 0);
            #else
                if (i == 28) return float4(0, 0, 0, 0);
            #endif
                return float4(-1, -1, -1, -1);
            }
            ENDCG
        }
    }
}
