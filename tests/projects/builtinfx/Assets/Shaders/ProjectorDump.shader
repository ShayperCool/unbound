// Writes unity_Projector (pixels 0-15) and unity_ProjectorClip (16-31), row by row, into the red
// channel of a 32x1 float target: a projector material whose draws cover the whole target.
Shader "Probe/ProjectorDump"
{
    SubShader
    {
        Tags { "Queue"="Geometry+100" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend One Zero
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4x4 unity_Projector;
            float4x4 unity_ProjectorClip;
            struct v2f { float4 pos : SV_POSITION; };
            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = float4(vertex.x * 2.0, vertex.y * 2.0, 0.5, 1.0);
                return o;
            }
            // Float arithmetic and one-hot selection only (no integer division or dynamic
            // indexing).
            float Element(float4x4 m, float e)
            {
                float r = floor(e / 4.0);
                float c = e - 4.0 * r;
                float4 row = r < 0.5 ? m[0] : r < 1.5 ? m[1] : r < 2.5 ? m[2] : m[3];
                return dot(row, float4(c < 0.5, abs(c - 1.0) < 0.5, abs(c - 2.0) < 0.5, c > 2.5));
            }
            float4 frag(v2f i) : SV_Target
            {
                float index = floor(i.pos.x);
                float v = index < 16.0 ? Element(unity_Projector, index) : Element(unity_ProjectorClip, index - 16.0);
                return float4(v, 0, 0, 1);
            }
            ENDCG
        }
    }
}
