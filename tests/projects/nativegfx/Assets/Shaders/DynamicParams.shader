Shader "Probe/NativeGfx/DynamicParams"
{
    // What a shader sees of a dynamically scaled render texture, as bytes (value / 64 or x 4):
    // pass 0 (a Blit from it) its _MainTex_TexelSize; pass 1 (drawn into it, clip space) and
    // pass 2 (a Blit into it) the _ScreenParams of the draw.
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
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return float4(v.xy, 0.5, 1); }
            float4 frag() : SV_Target { return float4(_ScreenParams.xy / 64, 0, 1); }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 frag(v2f_img i) : SV_Target { return float4(_ScreenParams.xy / 64, 0, 1); }
            ENDCG
        }
    }
}
