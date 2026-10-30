// Image effects: pass 0 inverts the source, pass 1 multiplies it by _Tint.
Shader "Probe/Invert"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} _Tint ("Tint", Color) = (1, 0.5, 0.5, 1) }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _Tint;
        float4 fragInvert(v2f_img i) : SV_Target { float4 c = tex2D(_MainTex, i.uv); return float4(1 - c.rgb, 1); }
        float4 fragTint(v2f_img i) : SV_Target { return tex2D(_MainTex, i.uv) * _Tint; }
        ENDCG
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragInvert
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragTint
            ENDCG }
    }
}
