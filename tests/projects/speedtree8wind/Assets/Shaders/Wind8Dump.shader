// Writes the SpeedTree 8 wind constants a renderer draws with: pixel x of a 30x1 target gets
// constant x (the 15 of the SpeedTreeWind constant buffer, then the 15 of SpeedTreeWindHistory).
// The mesh is a quad whose UVs cover [0, 1], so it covers the target.
Shader "Probe/Wind8Dump"
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
                float4 _ST_WindGlobal;
                float4 _ST_WindBranch;
                float4 _ST_WindBranchTwitch;
                float4 _ST_WindBranchWhip;
                float4 _ST_WindBranchAnchor;
                float4 _ST_WindBranchAdherences;
                float4 _ST_WindTurbulences;
                float4 _ST_WindLeaf1Ripple;
                float4 _ST_WindLeaf1Tumble;
                float4 _ST_WindLeaf1Twitch;
                float4 _ST_WindLeaf2Ripple;
                float4 _ST_WindLeaf2Tumble;
                float4 _ST_WindLeaf2Twitch;
                float4 _ST_WindFrondRipple;
            };

            cbuffer SpeedTreeWindHistory
            {
                float4 _ST_WindVectorHistory;
                float4 _ST_WindGlobalHistory;
                float4 _ST_WindBranchHistory;
                float4 _ST_WindBranchTwitchHistory;
                float4 _ST_WindBranchWhipHistory;
                float4 _ST_WindBranchAnchorHistory;
                float4 _ST_WindBranchAdherencesHistory;
                float4 _ST_WindTurbulencesHistory;
                float4 _ST_WindLeaf1RippleHistory;
                float4 _ST_WindLeaf1TumbleHistory;
                float4 _ST_WindLeaf1TwitchHistory;
                float4 _ST_WindLeaf2RippleHistory;
                float4 _ST_WindLeaf2TumbleHistory;
                float4 _ST_WindLeaf2TwitchHistory;
                float4 _ST_WindFrondRippleHistory;
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
                if (i == 1) return _ST_WindGlobal;
                if (i == 2) return _ST_WindBranch;
                if (i == 3) return _ST_WindBranchTwitch;
                if (i == 4) return _ST_WindBranchWhip;
                if (i == 5) return _ST_WindBranchAnchor;
                if (i == 6) return _ST_WindBranchAdherences;
                if (i == 7) return _ST_WindTurbulences;
                if (i == 8) return _ST_WindLeaf1Ripple;
                if (i == 9) return _ST_WindLeaf1Tumble;
                if (i == 10) return _ST_WindLeaf1Twitch;
                if (i == 11) return _ST_WindLeaf2Ripple;
                if (i == 12) return _ST_WindLeaf2Tumble;
                if (i == 13) return _ST_WindLeaf2Twitch;
                if (i == 14) return _ST_WindFrondRipple;
                if (i == 15) return _ST_WindVectorHistory;
                if (i == 16) return _ST_WindGlobalHistory;
                if (i == 17) return _ST_WindBranchHistory;
                if (i == 18) return _ST_WindBranchTwitchHistory;
                if (i == 19) return _ST_WindBranchWhipHistory;
                if (i == 20) return _ST_WindBranchAnchorHistory;
                if (i == 21) return _ST_WindBranchAdherencesHistory;
                if (i == 22) return _ST_WindTurbulencesHistory;
                if (i == 23) return _ST_WindLeaf1RippleHistory;
                if (i == 24) return _ST_WindLeaf1TumbleHistory;
                if (i == 25) return _ST_WindLeaf1TwitchHistory;
                if (i == 26) return _ST_WindLeaf2RippleHistory;
                if (i == 27) return _ST_WindLeaf2TumbleHistory;
                if (i == 28) return _ST_WindLeaf2TwitchHistory;
                return _ST_WindFrondRippleHistory;
            }
            ENDHLSL
        }
    }
}
