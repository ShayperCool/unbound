// No keyword declarations: a CG pass and a fixed-function pass.
Shader "Probe/KwPlain"
{
    Properties { _Color ("Color", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return _Color; }
            ENDCG
        }
        Pass { Color [_Color] }
    }
}
