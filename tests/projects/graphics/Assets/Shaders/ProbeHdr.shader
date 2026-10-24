Shader "Probe/Hdr"
{
    Properties { _Value ("Value", Vector) = (2, 0.5, -1, 1) }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Value;
            float4 frag(v2f_img i) : SV_Target { return _Value; }
            ENDCG
        }
    }
}
