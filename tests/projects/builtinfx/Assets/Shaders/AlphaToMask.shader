// Alpha to coverage: alpha falls from 1 to 0 across the quad's u.
Shader "Probe/AlphaToMask"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            AlphaToMask On
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.uv = uv;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { return fixed4(_Color.rgb, 1.0 - i.uv.x); }
            ENDCG
        }
    }
}
