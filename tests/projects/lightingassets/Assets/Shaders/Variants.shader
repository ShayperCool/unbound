// Keyword sets per pass for ShaderVariantCollection.ShaderVariant's checks: an untagged pass with
// two global sets, a ForwardBase pass with the light pass set, a ShadowCaster pass.
Shader "Probe/Variants"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Name "Plain"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ PLAIN_ON
            #pragma multi_compile _ RED_ON
            #pragma multi_compile _ ONE_A ONE_B
            #include "UnityCG.cginc"
            fixed4 _Color;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target
            {
                fixed4 c = _Color;
            #if RED_ON
                c.r = 1;
            #endif
            #if ONE_A || ONE_B
                c.b = 0.5;
            #endif
            #if PLAIN_ON
                c.g = 0;
            #endif
                return c;
            }
            ENDCG
        }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile _ RED_ON
            #pragma shader_feature_local _ BLUE_ON
            #include "UnityCG.cginc"
            fixed4 _Color;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target
            {
                fixed4 c = _Color;
            #if RED_ON
                c.r = 1;
            #endif
            #if BLUE_ON
                c.b = 1;
            #endif
                return c;
            }
            ENDCG
        }
        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #pragma multi_compile _ CASTER_ON
            #include "UnityCG.cginc"
            struct v2f { V2F_SHADOW_CASTER; };
            v2f vert(appdata_base v) { v2f o; TRANSFER_SHADOW_CASTER_NORMALOFFSET(o) return o; }
            float4 frag(v2f i) : SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
}
