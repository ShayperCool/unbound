// Writes stencil 1 where it draws, no color.
Shader "Probe/SamplerStates/StencilWrite"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always ColorMask 0
        Stencil { Ref 1 Comp Always Pass Replace }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return 0; }
            ENDCG
        }
    }
}
