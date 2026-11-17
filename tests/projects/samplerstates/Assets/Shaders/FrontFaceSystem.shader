// Two-sided, no depth: red where the fragment's SV_IsFrontFace input is true, green where it is
// false.
Shader "Probe/SamplerStates/FrontFaceSystem"
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
            float4 frag(bool front : SV_IsFrontFace) : SV_Target { return front ? float4(1, 0, 0, 1) : float4(0, 1, 0, 1); }
            ENDCG
        }
    }
}
