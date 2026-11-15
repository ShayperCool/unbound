// Draws a full-target triangle that shows what _Src samples at the target's centre.
Shader "Hidden/SrpMembersSample"
{
    Properties { _Src ("Source", 2D) = "black" {} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            sampler2D _Src;
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };
            v2f vert(appdata v) { v2f o; o.pos = float4(v.vertex.xy, 0.5, 1); return o; }
            float4 frag(v2f i) : SV_Target { return tex2D(_Src, float2(0.5, 0.5)); }
            ENDCG
        }
    }
}
