Shader "Probe/NativeGfx/MsaaStencil" {
 Properties { _Ref("Reference",Int)=37 }
 SubShader { Cull Off ZTest Always ZWrite On Stencil { Ref [_Ref] Comp Always Pass Replace } Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #pragma target 4.5
 #include "UnityCG.cginc"
 float4 frag(v2f_img i,out float depth:SV_Depth):SV_Target {depth=.25;return float4(.25,0,0,1);}
 ENDCG
 } }
}
