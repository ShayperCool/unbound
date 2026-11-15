Shader "Probe/StripShadowRead"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            struct v2f { float4 pos : SV_POSITION; };
            v2f vert(uint id : SV_VertexID) { v2f o; float2 p=float2((id<<1)&2,id&2);o.pos=float4(p*2-1,0.5,1);return o; }
            Texture2D<float> _ProbeShadowMap;
            float4 frag(v2f i) : SV_Target { float d=_ProbeShadowMap.Load(int3(i.pos.xy,0));return float4(d,d,d,1); }
            ENDHLSL
        }
    }
}
