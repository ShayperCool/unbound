Shader "Probe/Coverage" { SubShader { Pass { Cull Off ZWrite On
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
float4 vert(float4 p:POSITION):SV_POSITION { return UnityObjectToClipPos(p); }
float4 frag():SV_Target { return float4(0.75,0.5,0.25,0.625); }
ENDCG
} } }
