// The main light a ForwardBase pass is given: pixel x 0 holds _LightColor0, pixel x 1
// _WorldSpaceLightPos0.
Shader "Hidden/SunProbeForward"
{
    SubShader
    {
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
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
