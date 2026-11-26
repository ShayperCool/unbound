Shader "SmallStubs/Procedural"
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; };
            void setup() { }
            v2f vert(appdata v) { UNITY_SETUP_INSTANCE_ID(v); v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag(v2f i) : SV_Target { return fixed4(1, 0, 0, 1); }
            ENDCG
        }
    }
}
