Shader "Probe/DepthCopy"
{
    // The camera's sampled depth (_CameraDepthTexture) as a picture: its linear 0..1 depth in red,
    // 1 in green where the texture holds the far plane.
    Properties { _MainTex ("", 2D) = "black" {} }
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 frag(v2f_img i) : SV_Target
            {
                float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                float linear01 = Linear01Depth(raw);
                return float4(linear01, linear01 >= 0.999 ? 1 : 0, 0, 1);
            }
            ENDCG
        }
    }
}
