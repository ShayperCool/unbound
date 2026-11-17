// The sun a skybox is drawn with: pixel x 0 holds _LightColor0, pixel x 1
// _WorldSpaceLightPos0.
Shader "Hidden/SunProbeSky"
{
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }

            float4 frag(float4 position : SV_POSITION) : SV_Target
            {
                return position.x < 1 ? float4(_LightColor0.rgb, 1) : _WorldSpaceLightPos0;
            }
            ENDCG
        }
    }
}
