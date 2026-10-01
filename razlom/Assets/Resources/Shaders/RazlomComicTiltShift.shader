// Диорама комикс-рисовки (проба 01.10): размытие только у верхней и нижней кромки кадра, центр резкий.
// Ставит ComicStyleFeature после пост-обработки и до Screen Space Overlay — HUD не размывается.
// Вес повторяет ComicStyleRules.TiltShiftWeight строка в строку.
Shader "Hidden/Razlom/Comic Tilt Shift"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float4 _TiltSourceTexel;  // 1/w, 1/h исходного кадра
        float4 _TiltStep;         // шаг выборки размытия в uv
        float4 _TiltShape;        // резкая полоса, сила у кромки

        float3 Fetch(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb; }
        ENDHLSL

        // 0 — уменьшение в четыре раза: четыре билинейные выборки = среднее 4×4.
        Pass
        {
            Name "Tilt downsample"
            Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 t = _TiltSourceTexel.xy;
                float3 c = Fetch(uv + float2(-t.x, -t.y)) + Fetch(uv + float2(t.x, -t.y))
                         + Fetch(uv + float2(-t.x, t.y)) + Fetch(uv + float2(t.x, t.y));
                return half4(c * 0.25, 1.0);
            }
            ENDHLSL
        }

        // 1 — гауссово размытие по одной оси: девять выборок на ±2σ.
        Pass
        {
            Name "Tilt blur"
            Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                static const float weights[5] = { 1.0, 0.8824969, 0.6065307, 0.3246525, 0.1353353 };
                float3 c = Fetch(uv) * weights[0];
                float total = weights[0];
                [unroll] for (int k = 1; k < 5; k++)
                {
                    float2 offset = _TiltStep.xy * k;
                    c += (Fetch(uv + offset) + Fetch(uv - offset)) * weights[k];
                    total += 2.0 * weights[k];
                }
                return half4(c / total, 1.0);
            }
            ENDHLSL
        }

        // 2 — смешивание альфой прямо в кадр: в резкой полосе альфа 0 и кадр не меняется.
        Pass
        {
            Name "Tilt composite"
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // = ComicStyleRules.TiltShiftWeight
            float TiltWeight(float v)
            {
                float band = _TiltShape.x;
                float d = abs(saturate(v) * 2.0 - 1.0);
                float t = saturate((d - band) / max(1e-4, 1.0 - band));
                return t * t * (3.0 - 2.0 * t) * saturate(_TiltShape.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float weight = TiltWeight(uv.y);
                clip(weight - 1e-4);
                return half4(Fetch(uv), weight);
            }
            ENDHLSL
        }
    }
}
