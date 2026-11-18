// Draws opaque red wherever a canvas renderer is drawn, whatever its vertex colour: a drawn renderer
// is red, a culled one leaves the clear colour.
Shader "Probe/Solid"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            fixed4 frag() : SV_Target { return fixed4(1, 0, 0, 1); }
            ENDCG
        }
    }
}
