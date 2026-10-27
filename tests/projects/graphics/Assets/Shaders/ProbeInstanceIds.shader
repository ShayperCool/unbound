Shader "Probe/InstanceIds"
{
    Properties { _Color ("Color", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; };
            // A thin triangle per instance, side by side across the target; a procedural draw's
            // vertex ids, or a mesh's vertices (their x and y, the quad's corners at ±0.5).
            v2f vert(uint id : SV_VertexID, uint instance : SV_InstanceID, float4 vertex : POSITION)
            {
                v2f o;
                float2 p = _Color.a > 0.5
                    ? (id % 3 == 0 ? float2(0, 0) : id % 3 == 1 ? float2(0.5, 1) : float2(1, 0))
                    : vertex.xy + 0.5;
                o.pos = float4((p.x + instance) * 0.5 - 1, p.y * 1.5 - 0.75, 0.5, 1);
                o.color = float4(_Color.rgb * (instance + 1) / 4, 1);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
