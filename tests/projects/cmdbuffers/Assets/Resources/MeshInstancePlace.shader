Shader "Hidden/CmdBuffers/MeshInstancePlace"
{
    // An instanced quad placed by its raw instance ID (column ID of an 8-wide target, the
    // matrices ignored): _Mode 0 colors it by its instanced _Color, 1 by its raw ID (red) and
    // unity_InstanceID (green), (ID + 1) / 16.
    Properties { _Color ("Color", Vector) = (1,1,1,1) _Mode ("Mode", Float) = 0 }
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float4 color : TEXCOORD0; };
            float _Mode;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)
            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                uint raw = v.instanceID;
                o.pos = float4(-1 + 0.25 * raw + 0.25 * v.vertex.x, -1 + 2 * v.vertex.y, 0.5, 1);
                float4 ids = float4((raw + 1) / 16.0, (unity_InstanceID + 1) / 16.0, 0, 1);
                o.color = _Mode > 0.5 ? ids : UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                return o;
            }
            float4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
