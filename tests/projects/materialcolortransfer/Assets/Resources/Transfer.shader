Shader "Probe/MaterialColorTransfer" {
Properties {
 _Color("Color",Color)=(2,.5,-.25,3)
 [HDR] _Hdr("HDR",Color)=(2,.5,-.25,3)
 _Vector("Vector",Vector)=(2,.5,-.25,3)
 [Gamma] _GammaColor("Gamma Color",Color)=(2,.5,-.25,3)
 [Gamma][HDR] _GammaHdr("Gamma HDR",Color)=(2,.5,-.25,3)
 [Gamma] _GammaVector("Gamma Vector",Vector)=(2,.5,-.25,3)
 [Gamma] _GammaFloat("Gamma Float",Float)=2
 _Float("Float",Float)=2
 _Select("Select",Float)=0
}
SubShader { Pass { Cull Off ZTest Always ZWrite Off Blend Off
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
float4 _Color, _Hdr, _Vector, _GammaColor, _GammaHdr, _GammaVector;
float _GammaFloat, _Float, _Select;
float4 vert(float4 p:POSITION):SV_POSITION { return UnityObjectToClipPos(p); }
float4 frag():SV_Target {
 if(_Select<.5) return _Color;
 if(_Select<1.5) return _Hdr;
 if(_Select<2.5) return _Vector;
 if(_Select<3.5) return _GammaColor;
 if(_Select<4.5) return _GammaHdr;
 if(_Select<5.5) return _GammaVector;
 if(_Select<6.5) return float4(_GammaFloat,_GammaFloat,_GammaFloat,1);
 return float4(_Float,_Float,_Float,1);
}
ENDCG
} } }
