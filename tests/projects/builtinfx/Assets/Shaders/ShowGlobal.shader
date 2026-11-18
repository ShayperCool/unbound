Shader "Probe/Settings/ShowGlobal"
{
    // A camera texture as a picture: _Which 0 the depth-normals texture, 1 the motion vectors
    // (|xy| * 4 in red and green, 0.2 in blue).
    Properties { _MainTex ("", 2D) = "black" {} _Which ("", Float) = 0 }
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _CameraDepthNormalsTexture;
            sampler2D _CameraMotionVectorsTexture;
            float _Which;
            float4 frag(v2f_img i) : SV_Target
            {
                if (_Which < 0.5) return tex2D(_CameraDepthNormalsTexture, i.uv);
                return float4(abs(tex2D(_CameraMotionVectorsTexture, i.uv).rg) * 4, 0.2, 1);
            }
            ENDCG
        }
    }
}
