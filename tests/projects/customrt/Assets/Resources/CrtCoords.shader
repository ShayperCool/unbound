// Update shader: the vertex program and inputs are Unity's (UnityCustomRenderTexture.cginc).
// Pass 0 the texcoords, 1 the zone and texture info, 2 the parameters, 3 the cube direction.
Shader "CustomRT/Coords"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCustomRenderTexture.cginc"
        ENDCG
        Pass
        {
            Name "Coords"
            CGPROGRAM
            #pragma vertex CustomRenderTextureVertexShader
            #pragma fragment frag
            float4 frag(v2f_customrendertexture IN) : SV_Target { return float4(IN.localTexcoord.xy, IN.globalTexcoord.xy); }
            ENDCG
        }
        Pass
        {
            Name "Info"
            CGPROGRAM
            #pragma vertex CustomRenderTextureVertexShader
            #pragma fragment frag
            float4 frag(v2f_customrendertexture IN) : SV_Target { return float4(IN.primitiveID, _CustomRenderTextureInfo.x, _CustomRenderTextureInfo.y, _CustomRenderTextureInfo.w); }
            ENDCG
        }
        Pass
        {
            Name "Params"
            CGPROGRAM
            #pragma vertex CustomRenderTextureVertexShader
            #pragma fragment frag
            float4 frag(v2f_customrendertexture IN) : SV_Target { return float4(CustomRenderTextureParameters.xyz, _CustomRenderTextureInfo.z) + float4(0, 0, 0, IN.localTexcoord.z * 1000); }
            ENDCG
        }
        Pass
        {
            Name "Direction"
            CGPROGRAM
            #pragma vertex CustomRenderTextureVertexShader
            #pragma fragment frag
            float4 frag(v2f_customrendertexture IN) : SV_Target { return float4(IN.direction, _CustomRenderTextureCubeFace); }
            ENDCG
        }
    }
}
