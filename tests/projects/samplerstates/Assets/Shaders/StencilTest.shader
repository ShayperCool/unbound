// Draws red where the stencil is 1.
Shader "Probe/SamplerStates/StencilTest"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Stencil { Ref 1 Comp Equal }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return float4(1, 0, 0, 1); }
            ENDCG
        }
    }
}
