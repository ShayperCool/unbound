Shader "Probe/NativeGfx/LoadOutside"
{
    // An unfiltered load of _Src at the output pixel: pixels past _Src's size load outside it.
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
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
