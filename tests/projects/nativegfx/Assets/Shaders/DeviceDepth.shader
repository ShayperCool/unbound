Shader "Probe/NativeGfx/DeviceDepth"
{
 SubShader { Pass {
  ZTest Always Cull Off ZWrite On
  CGPROGRAM
  #pragma target 3.0
  #pragma vertex vert_img
  #pragma fragment frag
  #include "UnityCG.cginc"
  struct Out { float4 color : SV_Target; float depth : SV_Depth; };
  Out frag(v2f_img i) { Out o; o.color = float4(.125,.25,.5,1); o.depth = i.uv.x < .5 ? .25 : .75; return o; }
  ENDCG
 } }
}
