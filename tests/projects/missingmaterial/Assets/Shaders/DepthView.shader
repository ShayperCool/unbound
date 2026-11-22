// Draws the camera's depth texture (linear depth, scaled so that a few units read as mid grey).
Shader "Probe/DepthView"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _CameraDepthTexture;
            struct v2f { float4 pos : SV_POSITION; float4 screen : TEXCOORD0; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float depth = Linear01Depth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                return fixed4(depth * 8, depth * 8, depth * 8, 1);
            }
            ENDCG
        }
    }
}
