// Fixed-function SubShaders only, with LODs and tags.
Shader "Probe/KwFixed"
{
    Properties { _Color ("Color", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 400
        Pass { Tags { "LightMode"="Always" } Blend SrcAlpha OneMinusSrcAlpha Color [_Color] }
        Pass { Name "SECOND" Color (1, 0, 0, 1) }
        Pass { Color (0, 1, 0, 1) }
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Pass { Color [_Color] }
    }
    SubShader
    {
        LOD 100
        Pass { Color (0, 0, 1, 1) }
    }
}
