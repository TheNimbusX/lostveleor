// Живые детали главного меню: колыхание листьев, волос и ленты, течение воды.
//
// Что и насколько двигать, говорит маска на слой (полразрешения, линейная):
//   R — колыхание: бегущая волна смещает выборку, сильнее к кончикам;
//   G — течение: вода сдвигается вниз двумя выборками со сдвигом фазы в
//       половину периода и перетекает из одной в другую, поэтому у струи нет
//       видимого шва на повторе; поверх бегут вертикальные блики.
//
// ВРЕМЯ СВОЁ, А НЕ _Time. Встроенное _Time идёт по масштабированному времени,
// а на экране меню Time.timeScale равен нулю: анимация стояла бы на месте.
// MainMenuScene каждый кадр пишет глобальный _RazlomMenuTime.
Shader "Razlom/MenuSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _MaskTex ("Mask (R sway, G flow)", 2D) = "black" {}
        _SwayAmp ("Sway Amplitude (texels)", Float) = 0
        _SwaySpeed ("Sway Speed", Float) = 2
        _SwayFreq ("Sway Wave Frequency", Float) = 20
        _FlowSpeed ("Flow Speed (cycles/s)", Float) = 0.8
        _FlowPeriod ("Flow Period (texels)", Float) = 14
        _FlowGlint ("Flow Glint", Float) = 0.3
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "MenuSprite"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            // Глобальное, вне материала: одно на все слои меню.
            float _RazlomMenuTime;

            CBUFFER_START(UnityPerMaterial)
                half _SwayAmp;
                half _SwaySpeed;
                half _SwayFreq;
                half _FlowSpeed;
                half _FlowPeriod;
                half _FlowGlint;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float t = _RazlomMenuTime;
                float2 uv = input.uv;
                half2 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv).rg;

                // Колыхание: волна бежит вдоль ленты и листа, а не качает их
                // целиком, — так ткань выглядит тканью, а не картонкой.
                float phase = uv.x * _SwayFreq + uv.y * _SwayFreq * 0.6;
                float2 wave = float2(
                    sin(t * _SwaySpeed - phase),
                    0.5 * sin(t * _SwaySpeed * 0.77 - phase * 1.3 + 1.7));
                float2 uvSway = uv + wave * (_SwayAmp * mask.r) * _MainTex_TexelSize.xy;
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvSway);

                // Без if по маске: выборка текстуры внутри ветвления упирается в
                // производные для фильтрации, а экран меню эту пару лишних
                // выборок на пиксель даже не заметит.
                //
                // Выборка сверху — значит, картинка едет вниз. Период короткий:
                // длинный затаскивал бы в струю камень над ней.
                float p1 = frac(t * _FlowSpeed);
                float p2 = frac(t * _FlowSpeed + 0.5);
                float2 stepUV = float2(0.0, _FlowPeriod * _MainTex_TexelSize.y);
                half4 a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvSway + stepUV * p1);
                half4 b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvSway + stepUV * p2);
                half4 flowed = lerp(a, b, abs(2.0 * p1 - 1.0));

                // Узкие вытянутые блики, бегущие вниз: течение видно и там,
                // где нарисованная вода почти однородна.
                float n = valueNoise(float2(uv.x * 260.0, uv.y * 24.0 + t * _FlowSpeed * 6.0));
                flowed.rgb += smoothstep(0.62, 0.95, n) * _FlowGlint * flowed.a;
                color = lerp(color, flowed, mask.g);

                return color * input.color;
            }
            ENDHLSL
        }
    }
}
