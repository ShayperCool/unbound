// The texture dimension keywords in lower case.
Shader "Probe/DimsLower"
{
    Properties
    {
        _Lower2D ("Lower2D", 2d) = "white" {}
        _Lower3D ("Lower3D", 3d) = "" {}
        _LowerCube ("LowerCube", cube) = "" {}
        _Lower2DArray ("Lower2DArray", 2darray) = "" {}
        _LowerCubeArray ("LowerCubeArray", cubearray) = "" {}
        _LowerAny ("LowerAny", any) = "" {}
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
