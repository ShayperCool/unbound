Shader "Hidden/CmdBuffers/BindingMrt"
{
    // A full-target triangle at depth _Depth (0 near, 1 far) writing four colors: output i is
    // (_Tag, 50 * (i + 1), 30 + 40 * i, 255) / 255. ZWrite On, ZTest [_ZTest].
    Properties { _ZTest ("ZTest", Float) = 4 }
    SubShader
    {
        Pass
        {
            Cull Off ZWrite On ZTest [_ZTest]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"
            float _Tag, _Depth;
            struct v2f { float4 pos : SV_POSITION; };
            struct targets { float4 c0 : SV_Target0; float4 c1 : SV_Target1; float4 c2 : SV_Target2; float4 c3 : SV_Target3; };
            v2f vert(uint id : SV_VertexID)
            {
                float2 p = float2(id == 1u ? 3 : -1, id == 2u ? 3 : -1);
                v2f o;
                #if defined(UNITY_REVERSED_Z)
                float far = 0;
                #else
                float far = 1;
                #endif
                o.pos = float4(p, lerp(UNITY_NEAR_CLIP_VALUE, far, _Depth), 1);
                return o;
            }
            targets frag(v2f i)
            {
                targets t;
                t.c0 = float4(_Tag, 50, 30, 255) / 255.0;
                t.c1 = float4(_Tag, 100, 70, 255) / 255.0;
                t.c2 = float4(_Tag, 150, 110, 255) / 255.0;
                t.c3 = float4(_Tag, 200, 150, 255) / 255.0;
                return t;
            }
            ENDCG
        }
    }
}
