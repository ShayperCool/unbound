Shader "Probe/CubeArray"
{
    Properties { _Element ("Element", Float) = 1 }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma require cubearray
            #include "UnityCG.cginc"
            UNITY_DECLARE_TEXCUBEARRAY(_Cubes);
            float _Element;
            // Left half: the first cube, right half: the element; rows look along +x, -y, +z, -z.
            fixed4 frag(v2f_img i) : SV_Target
            {
                float row = floor(i.uv.y * 4);
                float3 dir = row < 1 ? float3(1, 0.2, 0.1) : row < 2 ? float3(0.1, -1, 0.2) : row < 3 ? float3(0.2, 0.1, 1) : float3(-0.1, 0.2, -1);
                float element = i.uv.x < 0.5 ? 0 : _Element;
                return UNITY_SAMPLE_TEXCUBEARRAY(_Cubes, float4(dir, element));
            }
            ENDCG
        }
    }
}
