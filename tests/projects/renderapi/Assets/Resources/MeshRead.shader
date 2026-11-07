Shader "Probe/MeshRead" {
SubShader { Pass {
ZTest Always Cull Off
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 4.5
#include "UnityCG.cginc"
ByteAddressBuffer MeshVertices;
ByteAddressBuffer MeshIndices;
struct v2f { float4 pos : SV_POSITION; };
v2f vert(uint id : SV_VertexID) {
    uint word = MeshIndices.Load((id / 2) * 4);
    uint index = (word >> ((id & 1) * 16)) & 65535;
    float3 position = asfloat(MeshVertices.Load3(index * 12));
    v2f o; o.pos = float4(position,1); return o;
}
float4 frag(v2f i) : SV_Target { return float4(.2,.6,.9,1); }
ENDCG
} }
}
