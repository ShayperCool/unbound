// Draws nothing (the projector dump quad's own material).
Shader "Probe/Blank"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return float4(vertex.x * 2.0, vertex.y * 2.0, 0.5, 1.0); }
            float4 frag() : SV_Target { return 0; }
            ENDCG
        }
    }
}
