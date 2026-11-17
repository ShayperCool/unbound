// Two-sided, no depth: red where the fragment's VFACE input is positive (a front face), green
// where it is not.
Shader "Probe/SamplerStates/FrontFaceVFace"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag(float face : VFACE) : SV_Target { return face > 0 ? float4(1, 0, 0, 1) : float4(0, 1, 0, 1); }
            ENDCG
        }
    }
}
