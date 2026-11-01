// Returns what a filtered float sample of _Src gives, all four channels, unchanged.
Shader "Probe/SampleTex"
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
            sampler2D _Src;
            float4 frag(v2f_img i) : SV_Target { return tex2D(_Src, i.uv); }
            ENDCG
        }
    }
}
