// Writes the draw's object-to-world translation (rgb) and its x scale (a).
Shader "LineProbe/ObjectMatrix"
{
    SubShader
    {
        Tags { "Queue" = "Geometry" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float4 m : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.m = float4(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23, unity_ObjectToWorld._m00);
                return o;
            }
            float4 frag(v2f i) : SV_Target { return i.m; }
            ENDCG
        }
    }
}
