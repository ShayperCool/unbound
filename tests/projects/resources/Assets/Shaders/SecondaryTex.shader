// Shows a sprite's secondary textures: red and green from _MaskTex, blue from the normal map's
// unpacked x, the main texture's alpha.
Shader "Probe/SecondaryTex"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _MaskTex ("Mask", 2D) = "gray" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _MaskTex, _NormalMap;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 mask = tex2D(_MaskTex, i.uv);
                float3 n = UnpackNormal(tex2D(_NormalMap, i.uv));
                return fixed4(mask.r, mask.g, n.x * 0.5 + 0.5, tex2D(_MainTex, i.uv).a);
            }
            ENDCG
        }
    }
}
