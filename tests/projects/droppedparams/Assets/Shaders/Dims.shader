// One property of every texture dimension ShaderLab declares, and two that are not textures.
Shader "Probe/Dims"
{
    Properties
    {
        _Float ("Float", Float) = 1
        _Tex2D ("Tex2D", 2D) = "white" {}
        _Tex3D ("Tex3D", 3D) = "" {}
        _TexCube ("TexCube", Cube) = "" {}
        _Tex2DArray ("Tex2DArray", 2DArray) = "" {}
        _TexCubeArray ("TexCubeArray", CubeArray) = "" {}
        _TexAny ("TexAny", Any) = "" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            fixed4 frag() : SV_Target { return 1; }
            ENDCG
        }
    }
}
