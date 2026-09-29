Shader "Razlom/Forest Acid Ground"
{
    // Земля вокруг живой лужи V6 (кадр владельца 10-bud-puddle-alive-b-vapour-crust).
    // Один шейдер — два слоя на той же сетке по рельефу, что и кислота:
    //   _Layer 0 «Трава темнеет» — умножение (Blend DstColor Zero): тёмная мокрая
    //     земля у кромки и вялая бурая трава дальше; настоящая текстура земли
    //     под ним остаётся видна, общий шейдер травы лагеря не трогаем;
    //   _Layer 1 «Корка» — плиты сухой потрескавшейся корки (премультипл. альфа):
    //     нарастают у кромки за жизнь лужи, после конца кислоты занимают всё
    //     место, откуда она ушла, и бледнеют вместе с _Fade.
    // Контур — та же функция Outline, что в Forest Acid Surface.
    Properties
    {
        _NoiseMap ("Пятна (CFXR noise clouds)", 2D) = "gray" {}
        _WetSoil ("Мокрая земля у кромки (множитель)", Color) = (.62,.50,.80,1)
        _Scorch ("Вялая трава (множитель)", Color) = (.84,.72,.88,1)
        _UnderAcid ("Под кислотой (множитель)", Color) = (.50,.42,.62,1)
        _CrustLight ("Корка: светлая плита", Color) = (.47,.36,.19,1)
        _CrustDark ("Корка: тёмная плита", Color) = (.37,.28,.14,1)
        _Crack ("Корка: трещина", Color) = (.21,.15,.08,1)
        _AgeSeconds ("Возраст от падения, с", Float) = 0
        _Dry ("Высыхание", Range(0,1)) = 0
        _Fade ("Видимость следа", Range(0,1)) = 1
        _Seed ("Номер лужи", Float) = 0
        _Radius ("Радиус кислоты, м", Float) = 1.2
        _Lift ("Над землёй, м", Float) = .03
        [Enum(Scorch,0,Crust,1)] _Layer ("Слой", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-16" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseMap_ST;
                half4 _WetSoil, _Scorch, _UnderAcid, _CrustLight, _CrustDark, _Crack;
                float _AgeSeconds, _Dry, _Fade, _Seed, _Radius, _Lift, _Layer, _SrcBlend, _DstBlend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            float Outline(float angle, float seed)
            {
                return 1 + .075 * sin(angle * 3 + seed * 1.7) + .05 * sin(angle * 5 - seed * 2.3) + .03 * sin(angle * 9 + seed * .9);
            }

            float Noise(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, uv).r;
            }

            float2 CellPoint(float2 cell)
            {
                float2 h = float2(dot(cell, float2(127.1, 311.7)), dot(cell, float2(269.5, 183.3)));
                return frac(sin(h) * 43758.5453);
            }

            // Плиты корки: x — от центра плиты, y — запас до трещины, z — случайное плиты.
            float3 Plates(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                float nearest = 8, second = 8;
                float2 owner = cell;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 o = g + CellPoint(cell + g) * .8 + .1 - f;
                    float d = dot(o, o);
                    if (d < nearest) { second = nearest; nearest = d; owner = cell + g; }
                    else if (d < second) second = d;
                }
                nearest = sqrt(nearest); second = sqrt(second);
                return float3(nearest, second - nearest, frac(sin(dot(owner, float2(12.9898, 78.233))) * 43758.5453));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                input.positionOS.y += _Lift;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.local = input.positionOS.xz;
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
                float n = Noise(p * .21 + s * .173);
                float n2 = Noise(p * .57 - s * .091);
                float q = r / (_Radius * Outline(angle, s));

                if (_Layer < .5)
                {
                    // Тёмное пятно бежит чуть впереди кислоты и за секунду темнеет до конца.
                    float front = lerp(.2, 1.46, smoothstep(0, .55, age)) + (n - .5) * .36;
                    float mask = 1 - smoothstep(front - .24, front, q);
                    float t = saturate((q - .98) / .45);
                    half3 factor = lerp(_WetSoil.rgb, _Scorch.rgb, t);
                    factor = lerp(_UnderAcid.rgb, factor, smoothstep(.9, 1, q));
                    factor *= lerp(.9, 1.07, n2);
                    float deepen = lerp(.4, 1, smoothstep(0, 1.1, age));
                    return half4(lerp(half3(1, 1, 1), saturate(factor), mask * deepen * _Fade), 1);
                }

                // Корка у кромки: основной серп со стороны от зерна и пятно поменьше.
                float crustAngle = s * 2.39996;
                float facing = dot(dir, float2(cos(crustAngle), sin(crustAngle)));
                float facing2 = dot(dir, float2(cos(crustAngle + 2.6), sin(crustAngle + 2.6)));
                float side = max(smoothstep(.1, .8, facing + (n - .5) * .5), smoothstep(.7, .97, facing2 + (n2 - .5) * .3) * .75);
                float grow = smoothstep(.9, 3.1, age);
                float outer = 1 + (.10 + .30 * side) * grow + (n2 - .5) * .1;
                // Внешний край корки мягкий и рваный (интеграция N, 29.09): с резким
                // краем и крупными плитами пояс читался мощёной дорожкой вокруг лужи.
                float band = smoothstep(.97, 1, q) * (1 - smoothstep(outer - .12 + (n - .5) * .08, outer + .02, q)) * smoothstep(.05, .3, side * grow);
                // После конца кислоты корка занимает место, откуда она ушла к центру.
                float receded = 1 - _Dry * 1.08 + (n - .5) * .25 * _Dry;
                float interior = (1 - smoothstep(1, 1.03, q)) * smoothstep(receded - .02, receded + .06, q) * step(.001, _Dry);
                float cover = max(band, interior) * _Fade;
                // Съёмка 29.09 (проверка захватом): одинаковые плиты со светлым загнутым краем
                // читались мостовой. Засохшая грязь — крупные плиты разного вида, внутри части
                // из них мелкие волосяные трещины; светлой «затирки» у шва нет.
                float3 plate = Plates(p * 3.4 + s * 3.7);
                float3 fine = Plates(p * 8.5 - s * 1.9);
                float fw = fwidth(plate.y), fwFine = fwidth(fine.y);
                // Плиты проявляются по одной, а не плёнкой.
                half alpha = saturate(cover * 1.8 - plate.z * .8);
                clip(alpha - .003);
                float crack = 1 - smoothstep(.05 - fw, .085 + fw, plate.y);
                // Мелкие трещины — не в каждой плите и слабее основных.
                float fineCrack = (1 - smoothstep(.028 - fwFine, .048 + fwFine, fine.y))
                                  * smoothstep(.35, .75, plate.z * .6 + n2 * .6) * .6;
                crack = max(crack, fineCrack);
                float curl = smoothstep(.06, .16, plate.y) * (1 - smoothstep(.16, .34, plate.y));
                half3 color = lerp(_CrustDark.rgb, _CrustLight.rgb, saturate(plate.z * .6 + n2 * .55));
                // Край плиты едва светлее, середина чуть темнее.
                color *= 1 + curl * .06 - plate.x * .08;
                color = lerp(color, _Crack.rgb, crack);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 sun = light.color / max(1e-3, max(light.color.r, max(light.color.g, light.color.b)));
                color *= lerp(.58, 1, light.shadowAttenuation) * lerp(half3(1, 1, 1), sun, .3);
                alpha *= lerp(.86, .96, crack);
                return half4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
