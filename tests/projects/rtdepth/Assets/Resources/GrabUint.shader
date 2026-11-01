// Draws _Color, grabs the target, then returns the grab texture loaded as unsigned integers,
// reinterpreted as floats.
Shader "Probe/GrabUint"
{
    Properties { _Color ("Color", Color) = (1, 1, 1, 1) }
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
            float4 _Color;
            float4 frag(v2f_img i) : SV_Target { return _Color; }
            ENDCG
        }
        GrabPass { }
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
            Texture2D<uint4> _GrabTexture;
            float4 frag(v2f_img i) : SV_Target { return asfloat(_GrabTexture.Load(int3(i.pos.xy, 0))); }
            ENDCG
        }
    }
}
