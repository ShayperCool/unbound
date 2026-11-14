Shader "Probe/NativeGfx/MsaaSingle"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #pragma target 4.5
 #include "UnityCG.cginc"
 Texture2D<float4> _Samples;
 float4 frag(v2f_img i):SV_Target {return _Samples.Load(int3(0,0,0));}
 ENDCG
 } }
}
