Shader "Hidden/Razlom/MinimapTerrain"
{
    Properties { _MainTex ("Terrain", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _View;

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 uv = _View.xy + input.uv * _View.zw;
                float2 pixel = abs(_MainTex_TexelSize.xy);
                float3 baseColor = tex2D(_MainTex, uv).rgb;
                // Приглушаем мелкую рябь травы, сохраняя крупные силуэты и дорожки.
                float3 neighbors = tex2D(_MainTex, uv + float2(pixel.x, 0)).rgb
                    + tex2D(_MainTex, uv - float2(pixel.x, 0)).rgb
                    + tex2D(_MainTex, uv + float2(0, pixel.y)).rgb
                    + tex2D(_MainTex, uv - float2(0, pixel.y)).rgb;
                float3 color = lerp(baseColor, neighbors * .25, .22);
                float luminance = dot(color, float3(.299, .587, .114));
                color = lerp(luminance.xxx, color, .82);
                // Светлые тёплые поверхности и мягкие зелёные тени связывают карту с HUD.
                color = pow(max(color, .001), .78) * .94 + float3(.075, .080, .052);
                color = lerp(color, color * float3(1.045, 1.015, .955), smoothstep(.35, .85, luminance));
                return float4(saturate(color), 1);
            }
            ENDCG
        }
    }
}
