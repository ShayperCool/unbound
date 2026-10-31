Shader "Probe/CameraLayers" { Properties { _Color("Color",Vector)=(1,1,1,1) } SubShader { Tags { "Queue"="Transparent" } Pass { Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
float4 _Color;
float4 vert(float4 p:POSITION):SV_POSITION { return UnityObjectToClipPos(p); }
float4 frag():SV_Target { return _Color; }
ENDCG
} } }
