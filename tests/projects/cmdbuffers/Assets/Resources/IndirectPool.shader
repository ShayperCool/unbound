Shader "Hidden/CmdBuffers/IndirectPool"
{
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
            v2f vert(uint id : SV_VertexID, uint instance : SV_InstanceID)
            {
                v2f o;
                float2 uv = float2((id << 1) & 2, id & 2);
                o.pos = float4(uv * 2 - 1, 0, 1);
                o.color = float4(_Tag / 255.0, (instance + 1) * 40.0 / 255.0, 1, 1);
                return o;
            }
            float4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            RWStructuredBuffer<uint> _Out : register(u1);
            float4 vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 uv = float2((id << 1) & 2, id & 2);
                return float4(uv * 2 - 1, 0, 1);
            }
            float4 frag() : SV_Target
            {
                _Out[0] = 3;
                _Out[1] = 2;
                _Out[2] = 0;
                _Out[3] = 23;
                return float4(1, 0, 0, 1);
            }
            ENDCG
        }
    }
}
