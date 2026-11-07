Shader "Hidden/CmdBuffers/ProceduralGrid"
{
    // Non-indexed vertex v is corner v % 6 of cell v / 6 (two triangles) in row SV_InstanceID of
    // an 8x8 grid of pixels. Red is _Tag / 255, green 40 * (instance + 1) / 255, blue
    // 20 * (cell + 1) / 255.
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
                uint cell = id / 6u, k = id % 6u;
                float2 corner = float2(k == 1u || k == 2u || k == 4u ? 1 : 0, k == 2u || k == 4u || k == 5u ? 1 : 0);
                float2 p = float2(cell % 8u, inst % 8u) + corner;
                v2f o;
                o.pos = float4(p * 0.25 - 1, 0, 1);
                o.color = float4(_Tag / 255.0, 40.0 * (inst + 1) / 255.0, 20.0 * (cell + 1) / 255.0, 1);
                return o;
            }
            float4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
