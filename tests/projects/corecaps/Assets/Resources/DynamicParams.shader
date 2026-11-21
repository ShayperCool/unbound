Shader "Probe/CoreCaps/DynamicParams"
{
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
            float4 _MainTex_TexelSize;
            float4 frag(v2f_img i) : SV_Target
            {
                return float4(_MainTex_TexelSize.zw / 64, _MainTex_TexelSize.xy * 4);
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 frag(v2f_img i) : SV_Target
            {
                return float4(_ScreenParams.xy / 64, 0, 1);
            }
            ENDCG
        }
    }
}
