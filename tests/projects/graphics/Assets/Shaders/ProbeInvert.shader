Shader "Probe/Invert"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 frag(v2f_img i) : SV_Target { fixed4 c = tex2D(_MainTex, i.uv); return fixed4(1 - c.rgb, c.a); }
            ENDCG
        }
    }
}
