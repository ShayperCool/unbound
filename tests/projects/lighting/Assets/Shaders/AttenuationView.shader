// Shows what the built-in pipeline gives a ForwardAdd pass: for point lights red is the
// attenuation texture at the squared light-space distance and green that squared distance; for
// spot lights red is the spot cookie, green the attenuation texture B and blue whether the point
// is in front of the light.
Shader "Probe/AttenuationView"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(0, 0, 0, 1); }
            ENDCG
        }
        Pass
        {
            Tags { "LightMode" = "ForwardAdd" }
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_lightpass
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };
            v2f vert(float4 v : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v);
                o.world = mul(unity_ObjectToWorld, v).xyz;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
            #if defined(POINT)
                float3 lc = mul(unity_WorldToLight, float4(i.world, 1)).xyz;
                float d2 = dot(lc, lc);
                return float4(tex2D(_LightTexture0, d2.rr).r, d2, 0, 1);
            #elif defined(SPOT)
                float4 lc = mul(unity_WorldToLight, float4(i.world, 1));
                return float4(UnitySpotCookie(lc), UnitySpotAttenuate(lc.xyz), lc.z > 0, 1);
            #else
                return float4(0, 0, 1, 1);
            #endif
            }
            ENDCG
        }
    }
}
