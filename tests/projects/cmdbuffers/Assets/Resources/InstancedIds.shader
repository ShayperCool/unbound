Shader "Hidden/CmdBuffers/InstancedIds"
{
    Properties { _Tint ("Tint", Vector) = (1, 1, 1, 1) _UseArray ("Use array", Float) = 0 }
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
            struct appdata { float4 vertex : POSITION; uint instanceID : SV_InstanceID; };
            struct v2f { float4 pos : SV_POSITION; float4 color : TEXCOORD0; };
            float4 _Palette[4];
            float _UseArray;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
            UNITY_INSTANCING_BUFFER_END(Props)
            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float4 color = _UseArray > 0 ? _Palette[v.instanceID % 4] : float4((v.instanceID + 1) / 8.0, 0, 0, 1);
                o.color = color * UNITY_ACCESS_INSTANCED_PROP(Props, _Tint);
                return o;
            }
            float4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
