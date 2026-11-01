// Writes built-in lighting globals as raw values, one per pixel column, into a float render
// target (IndirectSpecWitness reads them back). The column order is IndirectSpecWitness.Slots.
// A ForwardBase pass, so the draw gets the same per-frame/per-object lighting setup as a
// Standard ForwardBase draw. Slots 2-7 sample the reflection cubemap bound to the draw at a mip
// level past the last one (clamped to the last mip: one texel per face) along the six axes,
// decoded with unity_SpecCube0_HDR; slots 20-25 are the same samples undecoded.
Shader "Hidden/IndirectSpecDump"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull Off ZWrite Off ZTest Always

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            float4 Raw(float3 dir)
            {
                return UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, dir, 16.0);
            }

            float4 Decoded(float3 dir)
            {
                float4 raw = Raw(dir);
                return float4(DecodeHDR(raw, unity_SpecCube0_HDR), raw.a);
            }

            float4 frag(float4 pos : SV_POSITION) : SV_Target
            {
                int slot = (int)pos.x;
                if (slot == 0) return unity_IndirectSpecColor;
                if (slot == 1) return unity_SpecCube0_HDR;
                if (slot == 2) return Decoded(float3(1, 0, 0));
                if (slot == 3) return Decoded(float3(-1, 0, 0));
                if (slot == 4) return Decoded(float3(0, 1, 0));
                if (slot == 5) return Decoded(float3(0, -1, 0));
                if (slot == 6) return Decoded(float3(0, 0, 1));
                if (slot == 7) return Decoded(float3(0, 0, -1));
                if (slot == 8) return unity_SHAr;
                if (slot == 9) return unity_SHAg;
                if (slot == 10) return unity_SHAb;
                if (slot == 11) return unity_SHBr;
                if (slot == 12) return unity_SHBg;
                if (slot == 13) return unity_SHBb;
                if (slot == 14) return unity_SHC;
                if (slot == 15) return unity_AmbientSky;
                if (slot == 16) return unity_AmbientEquator;
                if (slot == 17) return unity_AmbientGround;
                if (slot == 18) return glstate_lightmodel_ambient;
                if (slot == 19) return unity_ColorSpaceDielectricSpec;
                if (slot == 20) return Raw(float3(1, 0, 0));
                if (slot == 21) return Raw(float3(-1, 0, 0));
                if (slot == 22) return Raw(float3(0, 1, 0));
                if (slot == 23) return Raw(float3(0, -1, 0));
                if (slot == 24) return Raw(float3(0, 0, 1));
                if (slot == 25) return Raw(float3(0, 0, -1));
                return float4(-1, -1, -1, -1);
            }
            ENDCG
        }
    }
}
