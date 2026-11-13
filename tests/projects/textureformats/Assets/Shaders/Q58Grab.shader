Shader "Probe/TexFormatsGrab"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        GrabPass { "_Q58Grab" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _Q58Grab;
            float4 frag(v2f_img i) : SV_Target { return tex2D(_Q58Grab, i.uv); }
            ENDHLSL
        }
    }
}
