// Unlit: _Color times _Tint (property blocks per material index).
Shader "Probe/TwoColors"
{
    Properties { _Color ("Color", Color) = (1, 1, 1, 1) _Tint ("Tint", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color, _Tint;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return _Color * _Tint; }
            ENDCG
        }
    }
}
