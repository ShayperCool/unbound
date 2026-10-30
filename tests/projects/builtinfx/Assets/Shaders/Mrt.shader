// Two color targets: _Color in the first, its inverse in the second.
Shader "Probe/Mrt"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct f2s { fixed4 a : SV_Target0; fixed4 b : SV_Target1; };
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            f2s frag()
            {
                f2s o;
                o.a = _Color;
                o.b = fixed4(1.0 - _Color.rgb, 1.0);
                return o;
            }
            ENDCG
        }
    }
}
