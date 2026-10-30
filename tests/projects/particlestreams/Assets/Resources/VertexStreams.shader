Shader "Probe/ParticleVertexStreams" {
 SubShader { Tags { "Queue"="Geometry" } Cull Off ZWrite On
  Pass { CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   struct appdata { float4 vertex:POSITION; float4 uv0:TEXCOORD0; float4 uv1:TEXCOORD1; float4 uv2:TEXCOORD2; };
   struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; };
   v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.color=float4(v.uv0.zw,v.uv1.x,1); return o; }
   fixed4 frag(v2f i):SV_Target { return i.color; }
  ENDCG }
 }
}
