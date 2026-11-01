// Returns an unfiltered float load of _Src at the output pixel, all four channels.
Shader "Probe/LoadFloat"
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
            #pragma target 4.5
            #include "UnityCG.cginc"
            Texture2D<float4> _Src;
            float4 frag(v2f_img i) : SV_Target { return _Src.Load(int3(i.pos.xy, 0)); }
            ENDCG
        }
    }
}
