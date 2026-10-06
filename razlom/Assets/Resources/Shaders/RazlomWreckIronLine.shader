Shader "Razlom/Wreck Iron Line"
{
    // ЛИНИЯ ВЫПАДА КРУШЕНИЯ v4 — целевой кадр 06.10
    // (ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/v4-lunge-simple.png).
    // V2 (06.10 вечер): владелец — «линейно»: V1 была ровной «гусеницей» с
    // зубьями через равный шаг. Теперь это ВОЛНА: толстая у точки удара и
    // сужается вперёд в острое жало, неровные вздутия по длине, ось чуть
    // гуляет, шипы редкие и разные (длинные отдельно, мелкая рвань чаще),
    // скошены назад по ходу вала. Рисованный язык волны сабли: рваное белое
    // ядро, голубой лёд, голубое тело, тень у края, тонкий тёмный обвод
    // постоянной ширины в пикселях, жёсткие пороги — без дымки.
    //
    // Меш — лента по земле (PelagVfxController.WreckLunge): u вдоль полосы
    // (0 — точка удара, 1 — конец полосы), v поперёк (0,5 — ось). Вид каждый
    // кадр даёт блоком свойств _Front (фронт Sim, доля длины), _Age (с от удара),
    // _Length (м), _Travel (с хода фронта) и _Seed (свой рисунок на каждый выпад).
    Properties
    {
        _Core ("Core", Color) = (1.25,1.3,1.35,1)
        _Light ("Light glow", Color) = (.55,.84,1.1,1)
        _Glow ("Glow", Color) = (.22,.52,1,1)
        _Shade ("Edge shade", Color) = (.12,.28,.78,1)
        _Outline ("Outline", Color) = (.03,.05,.16,1)
        _NoiseTex ("Noise (r)", 2D) = "gray" {}
        _Widths ("Widths, share of half-width (core, light, body, spike)", Vector) = (.11,.21,.32,.24)
        _Shape ("Shape (start width, end width, bulge, axis wobble)", Vector) = (1,.38,.28,.10)
        _SpikeFreq ("Big spikes per metre", Float) = 1.4
        _TipMeters ("Pointed head length (m)", Float) = 1.1
        _StartMeters ("Start taper (m)", Float) = .3
        _OutlinePx ("Outline width, px", Range(0,6)) = 2.2
        _HoldSeconds ("Hold after the front (s)", Float) = .18
        _ErodeSeconds ("Erode (s)", Float) = .35
        _FadeSeconds ("Fade (s)", Float) = .12
        _GlowAdd ("Core glow", Range(0,1)) = .25
        _Front ("Front (share of length)", Float) = 1
        _Age ("Age (s)", Float) = 0
        _Length ("Length (m)", Float) = 4
        _Travel ("Front travel (s)", Float) = .25
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+38" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Wreck Iron Line"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
            half4 _Core, _Light, _Glow, _Shade, _Outline;
            float4 _NoiseTex_ST, _Widths, _Shape;
            float _SpikeFreq, _TipMeters, _StartMeters, _OutlinePx, _HoldSeconds, _ErodeSeconds, _FadeSeconds, _GlowAdd;
            float _Front, _Age, _Length, _Travel, _Seed;
            CBUFFER_END
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float Noise(float2 p) { return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p).r; }

            float Hash(float n) { return frac(sin(n * 12.9898 + _Seed * 3.17) * 43758.5453); }

            /// Редкие зубья: в ячейке x зуб есть с вероятностью presence, высота — в степени (редкие длинные),
            /// центр и ширина случайны; зуб целиком внутри ячейки — край непрерывен (иначе fwidth рвёт обвод).
            float Spikes(float x, float presence, float power)
            {
                float cell = floor(x), f = frac(x);
                float on = step(1 - presence, Hash(cell + 9.7));
                float h = pow(Hash(cell + .17), power);
                float centre = .25 + .5 * Hash(cell + 3.1);
                float width = min(.16 + .3 * Hash(cell + 5.3), min(centre, 1 - centre));
                float tri = saturate(1 - abs(f - centre) / width);
                return on * h * tri * sqrt(tri);
            }

            float Step(float edge, float x)
            {
                float aa = max(fwidth(x) * .75, 1e-4);
                return saturate((x - edge) / aa + .5);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float u = i.uv.x;
                float m = u * _Length;                          // метры от точки удара
                float flick = _Age * 1.3;

                // Ось гуляет: плавный шум вдоль полосы, в долях полуширины.
                float wobble = (Noise(float2(m * .32 + _Seed * .61, .37)) - .5) * 2 * _Shape.w;
                float s = (i.uv.y - .5) * 2 - wobble;           // −1..1 поперёк от гуляющей оси
                float a = abs(s);
                // Свой рисунок шипов у каждой стороны; у оси стороны смешиваются плавно (скачок дал бы обвод по оси).
                float sideW = smoothstep(-.2, .2, s);

                // Профиль волны: толстая у точки удара, сужается вперёд; неровные вздутия; острая голова на фронте.
                float k = saturate(u);
                float profile = lerp(_Shape.x, _Shape.y, pow(k, .75));
                float bulge = 1 + _Shape.z * (Noise(float2(m * .5 + _Seed * 1.7, .71)) - .5) * 2;
                float front = min(_Front, 1.0);
                float tip = saturate((front * _Length - m) / max(.05, _TipMeters));
                float start = saturate(m / max(.05, _StartMeters));
                float taper = profile * bulge * sqrt(tip) * sqrt(start);

                // Шипы скошены назад (по ходу вала): ячейки сдвигаются с удалением от оси.
                float mm = m + a * .45;
                float spikesA = Spikes(mm * _SpikeFreq + 1.97 - flick * .2, .55, 1.4)
                    + .35 * Spikes(mm * _SpikeFreq * 3.7 + .84 + flick * .3, .6, 1.0);
                float spikesB = Spikes(mm * _SpikeFreq + 5.18 - flick * .2, .55, 1.4)
                    + .35 * Spikes(mm * _SpikeFreq * 3.7 + 2.2 + flick * .3, .6, 1.0);
                float spikes = lerp(spikesB, spikesA, sideW);
                float bodyEdge = (_Widths.z + _Widths.w * spikes) * taper;
                float dEdge = bodyEdge - a;

                // Рассыпание: после фронта держится, потом эрозия от точки удара к концу по шуму (жёсткий край).
                float local = _Age - u * _Travel;
                float erodeP = 1.05 * saturate((local - _HoldSeconds) / max(.01, _ErodeSeconds));
                float coarse = Noise(float2(m * .45 + .31 + _Seed * .23, a * .6 + .17));
                float dErode = (lerp(coarse, 1 - a * .7, .35) - erodeP) * 2.0;
                float pxEdge = dEdge / max(fwidth(dEdge), 1e-5);
                float pxErode = dErode / max(fwidth(dErode), 1e-5);
                float px = min(pxEdge, pxErode);
                float shape = saturate(px + .5) * step(m, front * _Length + .02);
                float body = saturate(px - _OutlinePx + .5);

                // Полосы поперёк: рваное белое ядро (толще у удара), голубой лёд, тело, тень у края.
                float jag = lerp(Spikes(m * 5.1 + 4.05 - flick, .7, 1.2), Spikes(m * 5.1 + 1.54 - flick, .7, 1.2), sideW) - .3;
                float coreEdge = _Widths.x * taper * (1 + .9 * jag + .8 * spikes);
                float lightEdge = _Widths.y * taper * (1 + .5 * jag + .9 * spikes);
                half3 col = lerp(_Glow.rgb, _Shade.rgb, Step(.72, a / max(bodyEdge, 1e-3)));
                col = lerp(_Light.rgb, col, Step(lightEdge, a));
                float core = 1 - Step(coreEdge, a);
                col = lerp(col, _Core.rgb, core);

                float fade = 1 - smoothstep(0, max(.01, _FadeSeconds), local - _HoldSeconds - _ErodeSeconds * .85);
                col = lerp(_Outline.rgb, col, body);
                float alpha = shape * lerp(_Outline.a, 1, body) * fade;
                half3 glow = _Core.rgb * core * body * shape * fade * _GlowAdd;
                return half4(col * alpha + glow, alpha);
            }
            ENDHLSL
        }
    }
}
