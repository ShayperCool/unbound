// Returns an unsigned integer load of _Src at the output pixel: x and y as floats.
Shader "Probe/LoadUint"
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
            Texture2D<uint2> _Src;
            float4 frag(v2f_img i) : SV_Target
            {
                uint2 s = _Src.Load(int3(i.pos.xy, 0));
                return float4(s.x, s.y, 0, 1);
            }
            ENDCG
        }
    }
}
