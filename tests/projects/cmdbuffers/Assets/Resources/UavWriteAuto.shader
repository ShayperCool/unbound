Shader "Hidden/CmdBuffers/UavWriteAuto"
{
    // As UavWrite, with the buffer at no declared register.
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"
            RWStructuredBuffer<uint> _Out;
            float _Tag;
            float4 vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 uv = float2((id << 1) & 2, id & 2);
                return float4(uv * 2 - 1, 0, 1);
            }
            float4 frag(float4 pos : SV_POSITION) : SV_Target
            {
                uint index = (uint)pos.y * 4 + (uint)pos.x;
                _Out[index] = (uint)_Tag * 1000 + index;
                return float4(1, 1, 1, 1);
            }
            ENDCG
        }
    }
}
