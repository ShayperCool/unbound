// Writes the matrices a draw sees into a 16x4 float target: pixel (4 * m + column, row) holds
// element [row, column] of matrix m (0 UNITY_MATRIX_VP, 1 UNITY_MATRIX_V, 2 UNITY_MATRIX_P,
// 3 unity_ObjectToWorld) in red, with m, row and column in green, blue and alpha. The quad covers
// the target whatever the matrices are.
Shader "Hidden/FrameStateMatrices"
{
    SubShader
    {
        Pass
        {
            Cull Off ZTest Always ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return float4(vertex.xy, 0.5, 1); }
            float4 frag(float4 position : SV_POSITION) : SV_Target
            {
                int x = (int)position.x, row = (int)position.y;
                int m = x / 4, column = x % 4;
                float4x4 matrices[4] = { UNITY_MATRIX_VP, UNITY_MATRIX_V, UNITY_MATRIX_P, unity_ObjectToWorld };
                return float4(matrices[m][row][column], m, row, column);
            }
            ENDCG
        }
    }
}
