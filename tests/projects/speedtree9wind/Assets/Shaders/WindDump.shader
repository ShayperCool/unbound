// Writes the SpeedTree wind constants a renderer draws with: pixel x of a 22x1 target gets
// constant x (the 11 of the SpeedTreeWind constant buffer, then the 11 of SpeedTreeWindHistory).
// The mesh is a quad whose UVs cover [0, 1], so it covers the target.
Shader "Probe/WindDump"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            cbuffer SpeedTreeWind
            {
                float4 _ST_WindVector;
                float4 _ST_TreeExtents_SharedHeightStart;
                float4 _ST_BranchStretchLimits;
                float4 _ST_Shared_NoisePosTurbulence_Independence;
                float4 _ST_Shared_Bend_Oscillation_Turbulence_Flexibility;
                float4 _ST_Branch1_NoisePosTurbulence_Independence;
                float4 _ST_Branch1_Bend_Oscillation_Turbulence_Flexibility;
                float4 _ST_Branch2_NoisePosTurbulence_Independence;
                float4 _ST_Branch2_Bend_Oscillation_Turbulence_Flexibility;
                float4 _ST_Ripple_NoisePosTurbulence_Independence;
                float4 _ST_Ripple_Planar_Directional_Flexibility_Shimmer;
            };

            cbuffer SpeedTreeWindHistory
            {
                float4 _ST_HistoryWindVector;
                float4 _ST_HistoryTreeExtents_SharedHeightStart;
                float4 _ST_HistoryBranchStretchLimits;
                float4 _ST_HistoryShared_NoisePosTurbulence_Independence;
                float4 _ST_HistoryShared_Bend_Oscillation_Turbulence_Flexibility;
                float4 _ST_HistoryBranch1_NoisePosTurbulence_Independence;
                float4 _ST_HistoryBranch1_Bend_Oscillation_Turbulence_Flexibility;
                float4 _ST_HistoryBranch2_NoisePosTurbulence_Independence;
                float4 _ST_HistoryBranch2_Bend_Oscillation_Turbulence_Flexibility;
                float4 _ST_HistoryRipple_NoisePosTurbulence_Independence;
                float4 _ST_HistoryRipple_Planar_Directional_Flexibility_Shimmer;
            };

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };

            float4 vert(appdata v) : SV_POSITION
            {
                return float4(v.uv * 2.0 - 1.0, 0.5, 1.0);
            }

            float4 frag(float4 pos : SV_POSITION) : SV_Target
            {
                int i = (int)pos.x;
                if (i == 0) return _ST_WindVector;
                if (i == 1) return _ST_TreeExtents_SharedHeightStart;
                if (i == 2) return _ST_BranchStretchLimits;
                if (i == 3) return _ST_Shared_NoisePosTurbulence_Independence;
                if (i == 4) return _ST_Shared_Bend_Oscillation_Turbulence_Flexibility;
                if (i == 5) return _ST_Branch1_NoisePosTurbulence_Independence;
                if (i == 6) return _ST_Branch1_Bend_Oscillation_Turbulence_Flexibility;
                if (i == 7) return _ST_Branch2_NoisePosTurbulence_Independence;
                if (i == 8) return _ST_Branch2_Bend_Oscillation_Turbulence_Flexibility;
                if (i == 9) return _ST_Ripple_NoisePosTurbulence_Independence;
                if (i == 10) return _ST_Ripple_Planar_Directional_Flexibility_Shimmer;
                if (i == 11) return _ST_HistoryWindVector;
                if (i == 12) return _ST_HistoryTreeExtents_SharedHeightStart;
                if (i == 13) return _ST_HistoryBranchStretchLimits;
                if (i == 14) return _ST_HistoryShared_NoisePosTurbulence_Independence;
                if (i == 15) return _ST_HistoryShared_Bend_Oscillation_Turbulence_Flexibility;
                if (i == 16) return _ST_HistoryBranch1_NoisePosTurbulence_Independence;
                if (i == 17) return _ST_HistoryBranch1_Bend_Oscillation_Turbulence_Flexibility;
                if (i == 18) return _ST_HistoryBranch2_NoisePosTurbulence_Independence;
                if (i == 19) return _ST_HistoryBranch2_Bend_Oscillation_Turbulence_Flexibility;
                if (i == 20) return _ST_HistoryRipple_NoisePosTurbulence_Independence;
                return _ST_HistoryRipple_Planar_Directional_Flexibility_Shimmer;
            }
            ENDHLSL
        }
    }
}
