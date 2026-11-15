Shader "Probe/StripShadowPosition"
{
    SubShader
    {
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float z : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.z=mul(unity_ObjectToWorld,v.vertex).z;return o; }
            float4 frag(v2f i) : SV_Target { return float4(i.z*.125+.5,0,0,1); }
            ENDCG
        }
        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            Cull Off ZWrite On ZTest Always
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float z : TEXCOORD0; };
            v2f vert(appdata_base v) { float4 p=mul(unity_ObjectToWorld,v.vertex);v2f o;o.pos=float4(p.xy,0.5,1);o.z=p.z;return o; }
            float frag(v2f i) : SV_Depth { return i.z*.125+.5; }
            ENDCG
        }
    }
}
