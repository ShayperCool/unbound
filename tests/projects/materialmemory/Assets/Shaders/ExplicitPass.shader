Shader "Hidden/Storage/ExplicitPass"
{
    Properties
    {
        _Color("Color", Color) = (1, 0, 0, 1)
        _Value("Value", Float) = 0
    }
    SubShader
    {
        LOD 100
        Pass
        {
            Name "Draw"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest Always
            CGPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            float4 _Color;
            float4 Vertex(float4 position : POSITION) : SV_POSITION { return position; }
            float4 Fragment() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
