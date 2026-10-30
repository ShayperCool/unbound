// A replacement shader: per RenderType a SubShader that shows the replaced material's _Color
// (Opaque), a fixed translucent green (Transparent) or blue (Custom, which no object has).
Shader "Probe/Replace"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            fixed4 frag() : SV_Target { return fixed4(_Color.rgb * float3(1.0, 0.4, 0.4), 1.0); }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            fixed4 frag() : SV_Target { return fixed4(0.2, 1.0, 0.2, 0.5); }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "RenderType"="Custom" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            fixed4 frag() : SV_Target { return fixed4(0.0, 0.0, 1.0, 1.0); }
            ENDCG
        }
    }
}
