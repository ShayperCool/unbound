// Returns an unsigned integer load of the (unbound) array texture _Arr as floats.
Shader "Probe/UintArray"
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
            Texture2DArray<uint4> _Arr;
            float4 frag(v2f_img i) : SV_Target { return float4(_Arr.Load(int4(i.pos.xy, 0, 0))); }
            ENDCG
        }
    }
}
