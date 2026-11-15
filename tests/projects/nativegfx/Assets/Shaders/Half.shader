Shader "Probe/NativeGfx/Half"
{
    // Half of _MainTex (color and alpha), for a target blitted onto itself.
    Properties { _MainTex ("", 2D) = "white" {} }
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 frag(v2f_img i) : SV_Target { return tex2D(_MainTex, i.uv) * 0.5; }
            ENDCG
        }
    }
}
