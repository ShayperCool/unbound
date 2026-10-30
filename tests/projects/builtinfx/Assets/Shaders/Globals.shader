// Shows built-in globals: unity_ShadowColor in rgb; red is 1 where BILLBOARD_FACE_CAMERA_POS is on
// (pass 1).
Shader "Probe/Globals"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZTest Always Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            float4 frag() : SV_Target { return float4(unity_ShadowColor.rgb, 1.0); }
            ENDCG
        }
        Pass
        {
            ZTest Always Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ BILLBOARD_FACE_CAMERA_POS
            #include "UnityCG.cginc"
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            float4 frag() : SV_Target
            {
            #ifdef BILLBOARD_FACE_CAMERA_POS
                return float4(1, 0, 0, 1);
            #else
                return float4(0, 1, 0, 1);
            #endif
            }
            ENDCG
        }
    }
}
