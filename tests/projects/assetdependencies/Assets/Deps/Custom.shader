Shader "Probe/Custom"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} _Other ("Other", 2D) = "black" {} _Locked ("Locked", 2D) = "gray" {} }
    SubShader
    {
        Pass
        {
            Name "MAIN"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "DepInclude.cginc"
            ENDCG
        }
    }
    Fallback "Diffuse"
}
