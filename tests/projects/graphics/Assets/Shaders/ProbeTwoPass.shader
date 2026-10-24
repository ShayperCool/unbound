Shader "Probe/TwoPass"
{
    Properties { _Color ("Color", Color) = (1, 0, 0, 1) }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            fixed4 frag(v2f_img i) : SV_Target { return _Color; }
            ENDCG
        }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 frag(v2f_img i) : SV_Target { return fixed4(i.uv.x, i.uv.y, 1, 1); }
            ENDCG
        }
    }
}
