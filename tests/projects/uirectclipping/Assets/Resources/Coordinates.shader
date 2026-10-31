Shader "Probe/ClipCoordinates"
{
 Properties { _ProbeMode("Mode", Float)=0 _ClipRect("Rect",Vector)=(-7,-8,9,10) _UIMaskSoftnessX("Soft X",Float)=11 _UIMaskSoftnessY("Soft Y",Float)=12 }
 SubShader { Tags { "Queue"="Transparent" } Pass {
 Cull Off ZWrite Off ZTest Always Blend Off
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
 #include "UnityCG.cginc"
 float4 _ClipRect; float _UIMaskSoftnessX, _UIMaskSoftnessY, _ProbeMode;
 struct Input { float4 vertex:POSITION; };
 struct Output { float4 vertex:SV_POSITION; float4 local:TEXCOORD0; float4 world:TEXCOORD1; };
 Output vert(Input v) { Output o; o.vertex=UnityObjectToClipPos(v.vertex); o.local=v.vertex; o.world=mul(unity_ObjectToWorld,v.vertex); return o; }
 float4 frag(Output i):SV_Target {
 if (_ProbeMode<.5) return float4(i.local.xyz/128,1);
 if (_ProbeMode<1.5) return float4(i.world.xyz/128,1);
 if (_ProbeMode<2.5) return _ClipRect/128;
 float keyword=0;
 #ifdef UNITY_UI_CLIP_RECT
 keyword=1;
 #endif
 if (_ProbeMode<3.5) return float4(_UIMaskSoftnessX/32,_UIMaskSoftnessY/32,keyword,1);
 float alpha=1;
 #ifdef UNITY_UI_CLIP_RECT
 alpha=step(_ClipRect.x,i.local.x)*step(_ClipRect.y,i.local.y)*step(i.local.x,_ClipRect.z)*step(i.local.y,_ClipRect.w);
 #endif
 return float4(alpha,alpha,alpha,alpha);
 }
 ENDCG
 } }
}
