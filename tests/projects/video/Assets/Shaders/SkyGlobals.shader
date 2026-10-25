// Writes the light variables a shader sees into a float render target, one vector per pixel along
// x: as a skybox (Background queue) and as a ForwardBase object pass, with no light in the scene.
Shader "Probe/SkyGlobals"
{
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Pass
        {
            ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag(float4 pos : SV_POSITION) : SV_Target
            {
                int i = (int)pos.x;
                if (i == 0) return _WorldSpaceLightPos0;
                if (i == 1) return _LightColor0;
                return float4(-1, -1, -1, -1);
            }
            ENDCG
        }
    }
}
