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
            float4 frag(v2f i) : SV_Target
            {
                int index = (int)floor(i.pos.x);
                int e = index % 16;
                int r = e / 4;
                int c = e % 4;
                float v = index < 16 ? unity_Projector[r][c] : unity_ProjectorClip[r][c];
                return float4(v, 0, 0, 1);
            }
            ENDCG
        }
    }
}
