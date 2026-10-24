Shader "Probe/Procedural"
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
            // A triangle from the vertex id alone, in world space around the origin.
            float4 vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 p = id == 0 ? float2(-0.75, -0.75) : id == 1 ? float2(0, 0.75) : float2(0.75, -0.75);
                return mul(UNITY_MATRIX_VP, float4(p, 0, 1));
            }
            fixed4 frag() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
