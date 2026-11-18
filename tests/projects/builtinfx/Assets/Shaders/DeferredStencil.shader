// A deferred object whose Deferred pass has its own stencil: by default it writes 2 (Comp Always,
// Pass Replace); _Ref, _Comp (CompareFunction), _Pass (StencilOp) and _WriteMask change it. Gray
// albedo.
Shader "Probe/DeferredStencil"
{
    Properties { _Ref ("Ref", Float) = 2 _Comp ("Comp", Float) = 8 _Pass ("Pass", Float) = 2 _WriteMask ("WriteMask", Float) = 255 }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="Deferred" }
            Stencil { Ref [_Ref] Comp [_Comp] Pass [_Pass] WriteMask [_WriteMask] }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.normal = UnityObjectToWorldNormal(v.normal); return o; }
            void frag(v2f i, out half4 g0 : SV_Target0, out half4 g1 : SV_Target1, out half4 g2 : SV_Target2, out half4 light : SV_Target3)
            {
                g0 = half4(0.5, 0.5, 0.5, 1);
                g1 = half4(0, 0, 0, 0);
                g2 = half4(normalize(i.normal) * 0.5 + 0.5, 1);
                light = half4(0, 0, 0, 1);
            }
            ENDCG
        }
    }
}
