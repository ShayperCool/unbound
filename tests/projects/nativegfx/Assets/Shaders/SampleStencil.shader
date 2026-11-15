Shader "Probe/NativeGfx/SampleStencil" {
 Properties { _Ref("Reference",Int)=37 _Mask("Sample mask",Int)=1 _Half("Left half only",Int)=0 }
 SubShader { Cull Off ZTest Always ZWrite On Stencil { Ref [_Ref] Comp Always Pass Replace } Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #pragma target 4.5
 #include "UnityCG.cginc"
 int _Mask, _Half;
 float4 frag(v2f_img i, uint sampleIndex:SV_SampleIndex, out float depth:SV_Depth):SV_Target {
  clip((_Mask & (1u << sampleIndex)) ? 1 : -1);
  if (_Half != 0) clip(.5 - i.uv.x);
  depth=.25;return float4(.25,0,0,1);
 }
 ENDCG
 } }
}
