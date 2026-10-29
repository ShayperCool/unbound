Shader "Probe/Grab"
{
    // The pixels under the quad, grabbed, inverted and read two texels right and one up.
    SubShader
    {
        Tags { "Queue" = "Transparent" }
        GrabPass {  }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _GrabTexture;
            float4 _GrabTexture_TexelSize;
            struct v2f { float4 pos : SV_POSITION; float4 grab : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.grab = ComputeGrabScreenPos(o.pos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.grab.xy / i.grab.w + _GrabTexture_TexelSize.xy * float2(2, 1);
                return fixed4(1 - tex2D(_GrabTexture, uv).rgb, 1);
            }
            ENDCG
        }
    }
}
