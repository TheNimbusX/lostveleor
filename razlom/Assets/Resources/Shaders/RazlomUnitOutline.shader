Shader "Hidden/Razlom/Unit Outline"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Silhouette ink"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 centre = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                // Внутри тела не ищем рёбра: складки, швы и отдельные меши
                // не должны превращаться в цветную сетку поверх персонажа.
                half outside = 1.0h - saturate(centre.a * 255.0h);
                if (outside <= 0.0h) return 0;

                float2 texel = _BlitTexture_TexelSize.xy;
                float scale = _BlitTexture_TexelSize.w / 1080.0;
                static const float2 directions[8] = {
                    float2(1,0), float2(-1,0), float2(0,1), float2(0,-1),
                    float2(0.7071,0.7071), float2(-0.7071,0.7071),
                    float2(0.7071,-0.7071), float2(-0.7071,-0.7071)
                };
                half coverage = 0;
                half3 ink = 0;
                [unroll] for (int ring = 1; ring <= 2; ring++)
                {
                    [unroll] for (int direction = 0; direction < 8; direction++)
                    {
                        half4 sampleMask = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp,
                            uv + directions[direction] * texel * (ring * scale));
                        half candidate = saturate(sampleMask.a * 3.0h - ring + 1.0h);
                        if (candidate > coverage)
                        {
                            coverage = candidate;
                            ink = sampleMask.rgb / max(sampleMask.a, 0.0001h);
                        }
                    }
                }
                return half4(ink, coverage * outside);
            }
            ENDHLSL
        }
    }
}
