// A sprite's vertex normal (_Which 0), tangent xyz (1) or tangent w and vertex color alpha (2),
// mapped from -1..1 to 0..1.
Shader "Probe/SpriteAttribs"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Which ("Which", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Which;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float4 c : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 x = _Which < 0.5 ? v.normal : _Which < 1.5 ? v.tangent.xyz : float3(v.tangent.w, v.color.a, 0);
                o.c = float4(x * 0.5 + 0.5, 1);
                return o;
            }
            float4 frag(v2f i) : SV_Target { return i.c; }
            ENDCG
        }
    }
}
