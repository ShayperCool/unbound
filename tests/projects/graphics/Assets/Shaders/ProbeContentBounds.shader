Shader "Probe/ContentBounds"
{
    // Finds which rows (pass 0) and columns (pass 1) of a texture hold any alpha: each output
    // pixel loops over _scanPixels samples across the texture, the loop bound a uniform.
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float3 uv : TEXCOORD0; };
            sampler2D _MainTex;
            float _scanPixels;
            v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = float3(v.uv, 1.0f / _scanPixels); return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float output = 0.0f;
                for (int index = 0; index < _scanPixels; index++)
                    if (output < 0.5f && tex2D(_MainTex, float2(i.uv.z * (float)index, i.uv.y)).a > 0.0f) output = 1.0f;
                return output;
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float3 uv : TEXCOORD0; };
            sampler2D _MainTex;
            float _scanPixels;
            v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = float3(v.uv, 1.0f / _scanPixels); return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float output = 0.0f;
                for (int index = 0; index < _scanPixels; index++)
                    if (output < 0.5f && tex2D(_MainTex, float2(i.uv.x, i.uv.z * (float)index)).a > 0.0f) output = 1.0f;
                return output;
            }
            ENDCG
        }
    }
}
