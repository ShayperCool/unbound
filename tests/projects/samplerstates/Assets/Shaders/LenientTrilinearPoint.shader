Shader "Probe/SamplerStates/LenientTrilinearPoint"
{
    // `_L` (two texels) through the wrongly spelt inline sampler `sampler_TrilinearPointClamp` at u 0.4, 1.4,
    // -0.4 and 1.9 (red, green, blue, alpha).
    Properties { _MainTex ("Main", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            Texture2D _L; SamplerState sampler_TrilinearPointClamp;
            #pragma vertex vert_img
            float S(float u) { return _L.Sample(sampler_TrilinearPointClamp, float2(u, 0.5)).r; }
            float4 frag(v2f_img i) : SV_Target { return float4(S(0.4), S(1.4), S(-0.4), S(1.9)); }
            ENDCG
        }
    }
}
