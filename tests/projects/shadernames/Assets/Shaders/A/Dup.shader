// One of four shaders of one name in different folders (README.md): draws red.
Shader "Unbound/Dup"
{
    Properties
    {
        _OnlyA ("Only A", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return float4(v.xy * 2, 0.5, 1); }
            fixed4 frag() : SV_Target { return fixed4(1,0,0, 1); }
            ENDCG
        }
    }
}
