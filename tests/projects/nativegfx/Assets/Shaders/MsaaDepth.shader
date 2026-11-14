Shader "Probe/NativeGfx/MsaaDepth"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #pragma target 4.5
 #include "UnityCG.cginc"
 Texture2DMS<float> _Samples;
 float4 frag(v2f_img i):SV_Target {
 uint w,h,s; _Samples.GetDimensions(w,h,s);
 return float4(_Samples.Load(int2(0,0),0),_Samples.Load(int2(0,0),s-1),w/16.0,s/16.0);
 }
 ENDCG
 } }
}
