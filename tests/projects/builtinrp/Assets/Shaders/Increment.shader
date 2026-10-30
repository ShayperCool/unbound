// The event log's clock: the source plus 8/255 in red, alpha 1.
Shader "Probe/Increment"
{
    Properties { _MainTex ("Texture", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 frag(v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, float2(0.5, 0.5));
                return float4(c.r + 8.0 / 255.0, 0, 0, 1);
            }
            ENDCG
        }
    }
}
