// What the camera's textures hold at the middle of the view, for the event log (pass 0: depth,
// raw and linear; pass 1: depth-normals, raw; pass 2: motion vectors).
Shader "Probe/SampleTextures"
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
            float d = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, float2(0.5, 0.5));
            return float4(d, Linear01Depth(d), 0, 1);
        }
        float4 fragDepthNormals(v2f_img i) : SV_Target
        {
            return tex2D(_CameraDepthNormalsTexture, float2(0.5, 0.5));
        }
        float4 fragMotion(v2f_img i) : SV_Target
        {
            return float4(tex2D(_CameraMotionVectorsTexture, float2(0.5, 0.5)).rg, 0, 1);
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
    }
}
