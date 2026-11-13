Shader "Probe/NativeGfx/ExternalView" {
 Properties { _Image ("Image",2D)="white"{} _Cube ("Cube",Cube)=""{} _Volume ("Volume",3D)=""{} }
 SubShader { Pass {
  Cull Off ZWrite Off ZTest Always
  CGPROGRAM
  #pragma target 4.5
  #pragma vertex vert_img
  #pragma fragment frag
  #include "UnityCG.cginc"
  Texture2D _Image; SamplerState sampler_Image;
  TextureCube _Cube; SamplerState sampler_Cube;
  Texture3D _Volume; SamplerState sampler_Volume;
  float4 _Image_TexelSize, _Cube_TexelSize, _Volume_TexelSize;
  float _Kind, _Meta, _Level;
  float4 frag(v2f_img input) : SV_Target {
   if (_Meta > 1.5) {
    uint w,h,d,levels;
    if (_Kind < .5) { _Image.GetDimensions(0,w,h,levels); return float4(w,h,1,levels); }
    if (_Kind < 1.5) { _Cube.GetDimensions(0,w,h,levels); return float4(w,h,1,levels); }
    _Volume.GetDimensions(0,w,h,d,levels); return float4(w,h,d,levels);
   }
   if (_Kind < .5) { if (_Meta > .5) return _Image_TexelSize; return _Image.SampleLevel(sampler_Image,float2(.4,.5),_Level); }
   if (_Kind < 1.5) { if (_Meta > .5) return _Cube_TexelSize; return _Cube.SampleLevel(sampler_Cube,float3(1,0,0),_Level); }
   if (_Meta > .5) return _Volume_TexelSize;
   return _Volume.SampleLevel(sampler_Volume,float3(.4,.5,.5),_Level);
  }
  ENDCG
 } }
}
