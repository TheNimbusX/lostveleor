Shader "Razlom/Wreck Painted"
{
    // КРУШЕНИЕ v4 — РИСОВАННЫЕ ТЕКСТУРЫ (06.10, лист ART/characters/pelag/wreck-look-2026-10-06/vfx-textures/
    // sheet-base-v1.png, нарезка artifacts/wreck/v4/vfx-painted/tools/cut_painted.py). Процедурные полосы V3/V4
    // владелец отверг («плоско, мыльно, как из примитивов»); волны сабли приняты, потому что это рисунок пака на
    // простом меше с нашим порогом и обводом — здесь то же самое для своего рисунка.
    //
    // Текстура — рисунок с прямой альфой (ключ по чёрному, цвет без чёрной каймы). Силуэт — порог по альфе _Cut
    // (мягкое свечение рисунка за порогом не просвечивает, а становится тонким тёмным обводом _Ink шириной в пикселях:
    // обвод берётся с размытого мипа). Внутри — цвет рисунка как есть, яркое чуть за порогом блума (_Hdr).
    //
    // Время: возраст частицы (поток AgePercent в TEXCOORD0.z × _LifeSeconds) или _Age блоком свойств (_AgeMode 1,
    // меши вида на земле). Раскрытие вдоль u (дуга маха, полоса выпада): голова бежит от _RevealFrom до конца за
    // _RevealSeconds или стоит на _Front (фронт Sim). Распад — порог без прозрачности: поле — альфа и яркость рисунка
    // (сначала уходят тёмные мазки, потом светлые, последними — белая кромка и звенья), раньше там, где голова прошла
    // раньше (хвост маха, тыл полосы); край распада тоже с обводом.
    //
    // Цвет формы — только синие места рисунка (маска «синее, чем красное и зелёное»), множитель _TintMul (база —
    // 1,1,1: свои цвета текстуры), _TintWhite осветляет их к жемчугу.
    Properties
    {
        _MainTex ("Painted (straight alpha)", 2D) = "white" {}
        _Ink ("Outline ink", Color) = (.03,.05,.16,1)
        _Cut ("Silhouette alpha cut", Range(0,1)) = .45
        _OutlineCut ("Outline alpha cut (blurred mip)", Range(0,1)) = .16
        _OutlineBias ("Outline mip bias", Range(0,4)) = 1.6
        _Gain ("Painted colour gain", Range(.5,2)) = 1
        _Hdr ("Bright gain over bloom", Range(0,1)) = .22
        _Glow ("Additive glow of the bright parts", Range(0,1)) = .10
        _TintMul ("Form tint multiplier (blue parts)", Color) = (1,1,1,1)
        _TintWhite ("Form whitening (blue parts)", Range(0,1)) = 0
        _AgeMode ("Age: 0 particle, 1 _Age", Float) = 0
        _Age ("Age (s), mode 1", Float) = 0
        _LifeSeconds ("Particle lifetime (s)", Float) = .3
        _RevealFrom ("Reveal: head start (u)", Float) = 1.02
        _RevealSeconds ("Reveal: seconds (0 = shown whole)", Float) = 0
        _RevealPow ("Reveal ease-out power", Float) = 2.2
        _UseFront ("Reveal by _Front", Float) = 0
        _Front ("Front (u), mode front", Float) = 1
        _TravelSeconds ("Seconds for the head to cross u 0..1 (erosion lag)", Float) = 0
        _BandMix ("Erosion field: alpha (0) .. brightness (1)", Range(0,1)) = .6
        _Along ("Erosion follows u (tail first)", Range(0,1)) = .35
        _ErodeFrom ("Erode from (s)", Float) = .12
        _ErodeTo ("Erode to (s)", Float) = .30
        _ErodePow ("Erode curve", Float) = 1
        _ErodeRim ("Outline width at the erosion edge (field units)", Range(0,.2)) = .045
        _CameraPush ("Push toward camera (m)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+41" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Wreck Painted"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float4 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 uv : TEXCOORD0; half4 color : COLOR; };

            CBUFFER_START(UnityPerMaterial)
            half4 _Ink, _TintMul;
            float4 _MainTex_ST;
            float _Cut, _OutlineCut, _OutlineBias, _Gain, _Hdr, _Glow, _TintWhite, _AgeMode, _Age, _LifeSeconds;
            float _RevealFrom, _RevealSeconds, _RevealPow, _UseFront, _Front, _TravelSeconds;
            float _BandMix, _Along, _ErodeFrom, _ErodeTo, _ErodePow, _ErodeRim, _CameraPush;
            CBUFFER_END
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                positionWS += GetWorldSpaceNormalizeViewDir(positionWS) * _CameraPush;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = v.uv.xyz;
                o.color = v.color;
                return o;
            }

            // Расстояние в пикселях экрана до порога: поле делится на свою производную (ширина края — 1 px).
            float Px(float d, float field) { return d / max(fwidth(field), 1e-5); }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv.xy;
                float u = saturate(uv.x);
                float age = _AgeMode > .5 ? _Age : saturate(i.uv.z) * _LifeSeconds;

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                half4 soft = SAMPLE_TEXTURE2D_BIAS(_MainTex, sampler_MainTex, uv, _OutlineBias);
                float lum = dot(tex.rgb, float3(.30, .55, .15));
                float lumSoft = dot(soft.rgb, float3(.30, .55, .15));

                // Раскрытие вдоль u: голова маха (по возрасту) или фронт вала (Sim); край рвётся по яркости мазков.
                float revealT = _RevealSeconds > 0 ? 1 - pow(1 - saturate(age / _RevealSeconds), _RevealPow) : 1;
                float head = _UseFront > .5 ? _Front : lerp(_RevealFrom, 1.02, revealT);
                float jag = (lum - .5) * .025;
                float dHead = head - u + jag;

                // Распад: каждое место стареет с мига, когда голова его прошла; поле — альфа и яркость рисунка.
                float lag = _UseFront > .5 ? u * _TravelSeconds : (_RevealSeconds > 0 ? saturate((u - _RevealFrom) / max(1e-3, 1.02 - _RevealFrom)) * _RevealSeconds * .6 : 0);
                float local = age - lag;
                float erode = pow(saturate((local - _ErodeFrom) / max(1e-3, _ErodeTo - _ErodeFrom)), _ErodePow);
                float field = lerp(tex.a, lum, _BandMix) * (1 - _Along) + _Along * u;
                float fieldSoft = lerp(soft.a, lumSoft, _BandMix) * (1 - _Along) + _Along * u;
                float cutE = erode * 1.08 - .04;
                // Нет распада — поле не режет вовсе (иначе тёмные мазки пропадали бы с рождения).
                float eBody = erode > 0 ? field - cutE : 1;
                float eShape = erode > 0 ? fieldSoft - cutE + _ErodeRim : 1;

                float dBody = min(min(Px(tex.a - _Cut, tex.a), Px(eBody, field)), Px(dHead, u));
                float dShape = min(min(Px(soft.a - _OutlineCut, soft.a), Px(eShape, fieldSoft)), Px(dHead + .004, u));
                float body = saturate(dBody + .5);
                float shape = saturate(max(dShape, dBody) + .5);

                // Цвет формы — только синие места рисунка.
                half3 paint = tex.rgb;
                float minRG = min(paint.r, paint.g);
                float blue = saturate((paint.b - minRG) * 4) * (1 - smoothstep(.78, .95, minRG));
                half3 tinted = paint * _TintMul.rgb;
                tinted = lerp(tinted, dot(tinted, half3(.33, .34, .33)).xxx * 1.05 + .08, _TintWhite);
                paint = lerp(paint, tinted, blue);
                float hot = smoothstep(.72, 1, lum);
                paint *= _Gain * (1 + _Hdr * hot);

                half3 col = lerp(_Ink.rgb, paint * i.color.rgb, body);
                float alpha = shape * i.color.a;
                half3 glow = paint * hot * body * _Glow * i.color.rgb;
                return half4(col * alpha + glow * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
