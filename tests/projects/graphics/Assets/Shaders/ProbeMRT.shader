Shader "Probe/MRT"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            struct targets { float4 first : SV_Target0; float4 second : SV_Target1; };
            // A quad over the left three quarters of the target, from the vertex id alone.
            v2f vert(uint id : SV_VertexID)
            {
                float2 corners[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };
                v2f o;
                o.uv = corners[id];
                o.pos = float4(o.uv.x * 1.5 - 1, o.uv.y * 2 - 1, 0.5, 1);
                return o;
            }
            targets frag(v2f i)
            {
                targets t;
                t.first = float4(i.uv.x, 0.25, 0, 1);
                t.second = float4(0, i.uv.y, 0.75, 1);
                return t;
            }
            ENDCG
        }
    }
}
