// Writes one of the per-object probe constants the built-in pipeline gives the draw (_Which:
// unity_SHAr..unity_SHC, unity_LightmapST, unity_ProbesOcclusion) into a float target.
Shader "Probe/SHDump"
{
    Properties { _Which ("Which", Float) = 0 }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            float _Which;
            struct v2f { float4 pos : SV_POSITION; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
            float4 frag(v2f i) : SV_Target
            {
                int w = (int)round(_Which);
                if (w == 0) return unity_SHAr;
                if (w == 1) return unity_SHAg;
                if (w == 2) return unity_SHAb;
                if (w == 3) return unity_SHBr;
                if (w == 4) return unity_SHBg;
                if (w == 5) return unity_SHBb;
                if (w == 6) return unity_SHC;
                if (w == 7) return unity_LightmapST;
                return unity_ProbesOcclusion;
            }
            ENDCG
        }
    }
}
