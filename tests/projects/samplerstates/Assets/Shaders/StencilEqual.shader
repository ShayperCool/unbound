// Draws _Color where the stencil equals _Ref.
Shader "Probe/SamplerStates/StencilEqual"
{
    Properties { _Ref ("Ref", Int) = 1 _Color ("Color", Color) = (1, 0, 0, 1) }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Stencil { Ref [_Ref] Comp Equal }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
