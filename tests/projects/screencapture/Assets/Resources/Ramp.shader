// A ramp: red follows u, green v, blue is 0.5, alpha 0.25 + 0.5 u (written, not blended).
Shader "Probe/Ramp"
{
    SubShader
    {
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 frag(v2f_img i) : SV_Target { return float4(i.uv.x, i.uv.y, 0.5, 0.25 + 0.5 * i.uv.x); }
            ENDCG
        }
    }
}
