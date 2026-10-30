// The camera's textures as images (pass 0: linear depth in red, raw depth in green; pass 1: the
// depth-normals texture as stored; pass 2: motion vectors, 0.5 + 10 x the motion; pass 3: motion
// vectors as stored).
Shader "Probe/ShowTextures"
{
    Properties { _MainTex ("Texture", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        sampler2D _CameraDepthNormalsTexture;
        sampler2D _CameraMotionVectorsTexture;
        float4 fragDepth(v2f_img i) : SV_Target
        {
            float d = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
            return float4(Linear01Depth(d), d, 0, 1);
        }
        float4 fragDepthNormals(v2f_img i) : SV_Target
        {
            return tex2D(_CameraDepthNormalsTexture, i.uv);
        }
        float4 fragMotion(v2f_img i) : SV_Target
        {
            float2 m = tex2D(_CameraMotionVectorsTexture, i.uv).rg;
            return float4(saturate(0.5 + m * 10.0), 0, 1);
        }
        float4 fragMotionRaw(v2f_img i) : SV_Target
        {
            return float4(tex2D(_CameraMotionVectorsTexture, i.uv).rg, 0, 1);
        }
        ENDCG
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragDepth
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragDepthNormals
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragMotion
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragMotionRaw
            ENDCG }
    }
}
