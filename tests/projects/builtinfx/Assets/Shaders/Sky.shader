// A skybox: the view direction as a color, tinted by _Color.
Shader "Probe/Sky"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.dir = vertex.xyz;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { return fixed4((normalize(i.dir) * 0.5 + 0.5) * _Color.rgb, 1.0); }
            ENDCG
        }
    }
}
