// Writes a flat color, the rasterized depth and a stencil reference.
Shader "Probe/DepthWrite"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Ref ("Stencil reference", Int) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Stencil { Ref [_Ref] Comp Always Pass Replace }
            ZWrite On
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color;
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            float4 frag() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
