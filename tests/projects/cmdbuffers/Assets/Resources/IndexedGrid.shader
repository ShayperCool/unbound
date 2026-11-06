Shader "Hidden/CmdBuffers/IndexedGrid"
{
    // Vertex v (taken modulo 81) is corner (v % 9, v / 9) of an 8x8 grid of pixels, raised by
    // four rows per instance: indices pick the cells drawn. Red is _Tag / 255, green
    // 40 * (instance + 1) / 255.
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            float _Tag;
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR0; };
            v2f vert(uint id : SV_VertexID, uint inst : SV_InstanceID)
            {
                uint v = id % 81u;
                float2 p = float2(v % 9u, v / 9u + inst * 4u);
                v2f o;
                o.pos = float4(p * 0.25 - 1, 0, 1);
                o.color = float4(_Tag / 255.0, 40.0 * (inst + 1) / 255.0, 0, 1);
                return o;
            }
            float4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
