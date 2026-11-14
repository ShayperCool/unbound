Shader "Probe/Targets/InstancingTarget"
{
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vertInstance
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #define vert unusedProbeVertex
            #include "TargetsCommon.hlsl"
            #undef vert
            struct appdata_instance
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            v2f vertInstance(appdata_instance v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.pos = float4(v.vertex.xy * 2 - 1, 0.5, 1);
                o.pos.x += unity_ObjectToWorld._m03 * 0.125;
                return o;
            }
            ENDHLSL
        }
    }
}
