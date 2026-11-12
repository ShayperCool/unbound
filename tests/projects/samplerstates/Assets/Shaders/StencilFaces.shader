// Writes the stencil by face, no color: front faces 1 (Replace), back faces 255 (Invert of 0).
Shader "Probe/SamplerStates/StencilFaces"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always ColorMask 0
        Stencil { Ref 1 CompFront Always PassFront Replace CompBack Always PassBack Invert }
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
