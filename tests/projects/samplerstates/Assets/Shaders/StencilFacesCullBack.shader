// As StencilFaces, culling back faces.
Shader "Probe/SamplerStates/StencilFacesCullBack"
{
    SubShader
    {
        Cull Back ZWrite Off ZTest Always ColorMask 0
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
