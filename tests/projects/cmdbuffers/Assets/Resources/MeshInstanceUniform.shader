Shader "Hidden/CmdBuffers/MeshInstanceUniform"
{
    Properties { _Tint ("Tint", Vector) = (1,1,1,1) _Weight ("Weight", Float) = 1 _Mode ("Mode", Float) = 0 }
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float4 color : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 _Palette[8];
            float _Mode;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
                UNITY_DEFINE_INSTANCED_PROP(float, _Weight)
                UNITY_DEFINE_INSTANCED_PROP(float4x4, _Warp)
            UNITY_INSTANCING_BUFFER_END(Props)
            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float4 world = mul(unity_ObjectToWorld, v.vertex);
                world.y += unity_InstanceID % 2;
                if (_Mode > 2.5) world.x += UNITY_ACCESS_INSTANCED_PROP(Props, _Warp)[0][3];
                o.pos = mul(UNITY_MATRIX_VP, world);
                float4 ids = float4((v.instanceID % 251 + 1) / 255.0,
                    (unity_InstanceID % 241 + 1) / 255.0, (unity_InstanceID / 241 + 1) / 255.0, 1);
                float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Tint);
                float weight = UNITY_ACCESS_INSTANCED_PROP(Props, _Weight);
                o.color = _Mode < 0.5 ? ids : (_Mode < 1.5 ? tint * weight : _Palette[unity_InstanceID % 8] * tint * weight);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                if (_Mode < 0.5) i.color.g = (unity_InstanceID % 241 + 1) / 255.0;
                return i.color;
            }
            ENDCG
        }
    }
}
