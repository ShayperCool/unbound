// Writes what a draw with a light probe proxy volume reads into a float target: row 0 its
// constants (unity_ProbeVolumeParams, the four rows of unity_ProbeVolumeWorldToObject,
// unity_ProbeVolumeSizeInv, unity_ProbeVolumeMin, a sample at the volume's centre), row 1 the
// draw's probe constants (unity_SHAr .. unity_SHC), then the volume's texels (unity_ProbeVolumeSH:
// texel (x, y, z) at pixel (x, 2 + 32 * z + y); 32 is the largest grid resolution). The quad covers the target whatever the
// matrices are.
Shader "Hidden/ProxyVolumeDump"
{
    SubShader
    {
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull Off ZTest Always ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return float4(vertex.xy, 0.5, 1); }
            float4 frag(float4 position : SV_POSITION) : SV_Target
            {
                int x = (int)position.x, row = (int)position.y;
                if (row == 0)
                {
            #if UNITY_LIGHT_PROBE_PROXY_VOLUME
                    if (x == 0) return unity_ProbeVolumeParams;
                    if (x == 1) return unity_ProbeVolumeWorldToObject[0];
                    if (x == 2) return unity_ProbeVolumeWorldToObject[1];
                    if (x == 3) return unity_ProbeVolumeWorldToObject[2];
                    if (x == 4) return unity_ProbeVolumeWorldToObject[3];
                    if (x == 5) return float4(unity_ProbeVolumeSizeInv, 0);
                    if (x == 6) return float4(unity_ProbeVolumeMin, 0);
                    // Sampled (which also binds the texture): the volume's centre.
                    if (x == 7) return UNITY_SAMPLE_TEX3D_SAMPLER(unity_ProbeVolumeSH, unity_ProbeVolumeSH, float3(0.5, 0.5, 0.5));
            #endif
                    return float4(-2, -2, -2, -2);
                }
                if (row >= 2)
                {
                    int t = row - 2;
            #if UNITY_LIGHT_PROBE_PROXY_VOLUME
                    return unity_ProbeVolumeSH.Load(int4(x, t % 32, t / 32, 0));
            #endif
                    return float4(-1, -1, -1, -1);
                }
                if (x == 0) return unity_SHAr;
                if (x == 1) return unity_SHAg;
                if (x == 2) return unity_SHAb;
                if (x == 3) return unity_SHBr;
                if (x == 4) return unity_SHBg;
                if (x == 5) return unity_SHBb;
                if (x == 6) return unity_SHC;
                return float4(-3, -3, -3, -3);
            }
            ENDCG
        }
    }
}
