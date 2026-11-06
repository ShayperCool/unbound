Shader "Hidden/CmdBuffers/IndirectGrid"
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
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
            StructuredBuffer<float3> _Positions;
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR0; };
            v2f vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                InitIndirectDrawArgs(0);
                uint cmd = GetCommandID(0);
                uint inst = GetIndirectInstanceID(instance);
                float2 p = _Positions[GetIndirectVertexID(vertex)].xy;
                p.y += inst * 4u;
                v2f o;
                o.pos = float4(p * 0.25 - 1, 0, 1);
                o.color = float4((20u + cmd) / 255.0, (40u + inst) / 255.0,
                    (60u + GetIndirectInstanceID_Base(instance) + GetIndirectInstanceCount()) / 255.0, 1);
                return o;
            }
            float4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
