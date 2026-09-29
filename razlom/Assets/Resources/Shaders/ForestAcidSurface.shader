Shader "Razlom/Forest Acid Surface"
{
    // Мутная кислота живой лужи V6 (кадр владельца 10-bud-puddle-alive-b-vapour-crust).
    // Лежит на сетке по рельефу (ForestPuddleSurface), координаты — метры от
    // центра лужи в осях объекта. Контур — те же лопасти от зерна, что у
    // тёмной травы и корки (Forest Acid Ground). Всё движение — функция
    // возраста Sim: растекание за 9 тиков, разводы, рябь от упавших капель
    // и лопнувших пузырей, кольцо каждого тика кислоты, высыхание к центру.
    Properties
    {
        _NoiseMap ("Разводы (CFXR perlin smudge)", 2D) = "gray" {}
        _Deep ("Густая муть", Color) = (.24,.25,.04,1)
        _Body ("Кислота", Color) = (.38,.38,.09,1)
        _Sheen ("Светлые разводы", Color) = (.58,.59,.18,1)
        _Glint ("Мокрый блик", Color) = (.86,.90,.55,1)
        _Meniscus ("Тёмная кромка", Color) = (.17,.16,.04,1)
        _AgeSeconds ("Возраст от падения, с", Float) = 0
        _Opacity ("Видимость", Range(0,1)) = 1
        _Dry ("Высыхание", Range(0,1)) = 0
        _Seed ("Номер лужи", Float) = 0
        _Radius ("Радиус кислоты, м", Float) = 1.2
        _Lift ("Над землёй, м", Float) = .05
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-5" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // Прозрачная поверхность берёт тень из карты теней, а не из экранной.
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseMap_ST;
                half4 _Deep, _Body, _Sheen, _Glint, _Meniscus;
                float _AgeSeconds, _Opacity, _Dry, _Seed, _Radius, _Lift;
            CBUFFER_END
            // Рябь: xy — точка, z — возраст начала, w — сила (капли шлепка).
            float4 _Ripples[8];
            // Лопнувшие пузыри: xy — точка, z — возраст первого хлопка, w — период.
            float4 _PopRipples[4];

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 local : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            // Контур лужи в долях среднего радиуса: три лопасти от зерна.
            // Та же функция стоит в Forest Acid Ground — кромки совпадают.
            float Outline(float angle, float seed)
            {
                return 1 + .075 * sin(angle * 3 + seed * 1.7) + .05 * sin(angle * 5 - seed * 2.3) + .03 * sin(angle * 9 + seed * .9);
            }

            float Noise(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, uv).r;
            }

            void Ripple(float2 p, float2 center, float t, float strength, inout float2 slope, inout float bright)
            {
                if (t <= 0 || t >= 1.1 || strength <= 0) return;
                float2 offset = p - center;
                float dist = length(offset);
                float width = .045 + t * .05;
                float x = (dist - (.03 + t * .5)) / width;
                float fall = 1 - t / 1.1;
                float envelope = exp(-x * x) * strength * fall * fall;
                float wave = sin(x * 3.1);
                slope += offset / max(dist, .001) * wave * envelope;
                bright += envelope * (.5 + .5 * wave);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float2 p = input.positionOS.xz;
                float age = max(0, _AgeSeconds);
                float phase = age * 2.2 + _Seed * .73;
                // Густая жидкость медленно дышит, оставаясь на рельефе.
                float swell = sin(p.x * 5.4 + phase) * sin(p.y * 4.6 - phase * .7);
                input.positionOS.y += _Lift + .01 * (1 + swell) * saturate(age * 5) * (1 - _Dry);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.local = p;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.local;
                float r = length(p);
                float2 dir = p / max(r, 1e-4);
                float angle = atan2(p.y, p.x);
                float s = _Seed;
                float age = max(0, _AgeSeconds);
                float n = Noise(p * .19 + s * .113);
                float n2 = Noise(p * .47 - s * .071);
                float q = r / (_Radius * Outline(angle, s) * (1 + (n - .5) * .07));

                // Фронт разливается от точки падения языками за 9 тиков созревания.
                float spread = saturate(age / .30);
                float tongues = sin(angle * 3 + s) * .12 + sin(angle * 7 - s) * .07;
                float front = lerp(.04, 1, smoothstep(0, 1, spread)) + tongues * (1 - spread) * .8;
                // Высыхание: кислота уходит к центру пятнами и оставляет корку.
                float wet = front * (1 - _Dry) - (n2 - .5) * .35 * _Dry;
                float inside = 1 - smoothstep(wet - .035, wet, q);
                float edge = saturate(q / max(wet, .001));

                // Медленные разводы мути и светлые полосы.
                float2 flow = float2(age * .035, -age * .027);
                float marble = Noise(p * .33 + flow + s * .31);
                float streak = Noise(p * .74 - flow * 1.4 - s * .23);
                half3 albedo = lerp(_Deep.rgb, _Body.rgb, smoothstep(.2, .7, marble));
                albedo = lerp(albedo, _Sheen.rgb, smoothstep(.6, .92, streak) * .5);
                // Кромка: тёмный мениск снаружи и светлая мокрая линия внутри.
                float meniscus = smoothstep(.86, .985, edge);
                float lip = smoothstep(.72, .86, edge) * (1 - smoothstep(.86, .95, edge));
                albedo = lerp(albedo, _Meniscus.rgb, meniscus * .7);
                albedo += _Sheen.rgb * lip * .18;

                // Рябь от капель шлепка и лопнувших пузырей.
                float2 slope = 0;
                float bright = 0;
                [unroll] for (int k = 0; k < 8; k++)
                    Ripple(p, _Ripples[k].xy, age - _Ripples[k].z, _Ripples[k].w, slope, bright);
                [unroll] for (int j = 0; j < 4; j++)
                {
                    float4 pop = _PopRipples[j];
                    float t = age - pop.z;
                    if (t > 0 && pop.w > 0) Ripple(p, pop.xy, fmod(t, pop.w), .6 * (1 - _Dry), slope, bright);
                }
                // Тик кислоты (раз в 15 тиков от созревания): светлое кольцо от центра к кромке.
                float pulseAge = age - .30;
                if (pulseAge >= 0)
                {
                    float beat = frac(pulseAge / .5);
                    float x = (q - beat * 1.05) / .09;
                    float band = exp(-x * x) * (1 - beat) * (1 - _Dry);
                    bright += band * .9;
                    slope += dir * band * sin(x * 3.1) * .6;
                }

                float phase = age * 2.2 + s * .73;
                float current = sin(p.x * 5.2 + phase + sin(p.y * 3.6 - phase * .4));
                float eddy = sin(p.y * 4.4 - phase * .67 + sin(p.x * 4 + phase));
                float3 normal = normalize(input.normalWS + float3(current * .18 + slope.x * .55, 0, eddy * .18 + slope.y * .55));

                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = lerp(.55, 1, light.shadowAttenuation);
                half3 sun = light.color / max(1e-3, max(light.color.r, max(light.color.g, light.color.b)));
                half diffuse = saturate(dot(normal, light.direction));
                float3 view = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                float halfway = saturate(dot(normal, SafeNormalize(light.direction + view)));
                half glint = pow(halfway, 60) * .9 + pow(halfway, 12) * .06;
                half3 color = albedo * (.72 + .28 * diffuse) * lerp(half3(1, 1, 1), sun, .35) * shadow;
                color += _Sheen.rgb * bright * .35 * shadow;
                color += _Glint.rgb * glint * shadow * (1 - meniscus * .5);
                // Удар плода: короткое оседающее кольцо.
                color += _Sheen.rgb * max(0, exp2(-age * 6) * .08 * sin(r * 22 - age * 22));
                // Сохнущая кислота темнеет и мутнеет.
                color = lerp(color, color * .6, _Dry);

                half alpha = inside * lerp(.93, .74, smoothstep(.55, 1, edge)) * _Opacity * (1 - _Dry * .35);
                clip(alpha - .003);
                return half4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
