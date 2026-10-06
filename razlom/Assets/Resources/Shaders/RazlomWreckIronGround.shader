// Крушение «холодное железо» (06.10, V7): разбитая земля круга удара и полосы, свет из трещин.
// Меш лежит на земле (вершины по высоте уступов, пишет вид при ударе), прозрачный, premultiplied:
// тёмная земля и тёмные трещины — альфой, холодный голубой свет — добавкой поверх (HDR, под bloom).
// БЕЗ МОЛНИЙ: тонкие линии трещин темнят землю, свет выходит из них мягким свечением (сеть, размытая
// мип-уровнем), насыщенный #4FA8FF, в жар — #9CD8FF.
// V7 (разбор владельца 06.10 «земля по краям и форма слишком чёткая»): ни одного ровного края —
//  • круг: вместо ровного диска — рваное пятно Hovl Crater2 (повёрнуто на удар, uv1.z) поверх тёмной
//    воронки Crater40 и рисованная «звезда» трещин Hovl Crack, выбегающая за кромку; свет — трещины Crater19;
//  • полоса: край ходит шумом камней (цвет вершины g — доля полуширины) и рвётся шумом CFXR, концы рваные
//    (r — доля от конца полосы); сеть трещин Crack4 выбегает за край тёмными ветками на разную длину;
//  • kind 3 — рваное пятно Crater2 (альфа с порогом: тело и отлетевшие куски) вдоль края полосы и по
//    кромке круга. Белый RGB Crater2 и дымка альфы Crater40 у края фактуры не берутся (иначе — диски).
// Вершина знает, когда до неё дошёл удар (uv1.x — секунды от удара): до этого её нет, в миг прихода
// трещины ярче (жар), остывают, через _Hold — гаснут за _FadeTime.
// uv1.y — вид куска: 0 — полоса (uv1.z — полуширина, м), 1 — круг (uv0.xy — 0…1 по кругу 2,2 R, uv1.z —
// поворот пятна, доля оборота), 2 — паз под звеном (uv0.xy — метры от центра звена; uv1.z — 1, если ребром),
// 3 — пятно, 4 — трещина (uv0.xy — фактура); uv0.zw — метры у всех; uv1.w — множитель света; a — кромка.
Shader "Razlom/Wreck Iron Ground"
{
    Properties
    {
        _CrackTex ("Lane Cracks (R)", 2D) = "black" {}
        _DirtTex ("Crater Dirt (A)", 2D) = "black" {}
        _CraterCrackTex ("Crater Cracks (R)", 2D) = "black" {}
        _SplatTex ("Broken Ground Splat (A soft, R hard)", 2D) = "black" {}
        _StarTex ("Crater Crack Star (A)", 2D) = "black" {}
        _NoiseTex ("Edge Noise (R)", 2D) = "gray" {}
        _DarkColor ("Dark Earth", Color) = (0.11,0.075,0.055,1)
        _DarkAlpha ("Dark Alpha", Range(0,1)) = 0.78
        _CrackDark ("Crack Darkness", Range(0,1)) = 0.9
        [HDR] _GlowColor ("Glow Color", Color) = (0.31,0.66,1.0,1)
        [HDR] _HotColor ("Hot Color", Color) = (0.61,0.85,1.0,1)
        _GlowBase ("Glow Steady", Float) = 1.1
        _GlowHot ("Glow Hot", Float) = 1.4
        _GlowBlur ("Glow Blur (mip bias)", Float) = 2.6
        _SpotGlow ("Link Gap Glow", Float) = 2.2
        _CraterGlow ("Crater Pool Glow", Float) = 0.6
        _CraterCrackGlow ("Crater Crack Glow", Float) = 1.6
        _LaneEdgeGlow ("Lane Edge Glow Share", Range(0,1)) = 0.3
        _CrackReach ("Lane Cracks Beyond Edge (of half width)", Float) = 0.7
        _SplatScale ("Crater Splat Scale (of 2.2R)", Float) = 0.85
        _StarScale ("Crater Star Scale (of 2.2R)", Float) = 0.8
        _LinkA ("Link Axis Radius (m)", Float) = 0.231
        _LinkC ("Link Axis Half Straight (m)", Float) = 0.3
        _LinkTube ("Link Tube Radius (m)", Float) = 0.094
        _Cool ("Cool Seconds", Float) = 0.22
        _Hold ("Hold Seconds", Float) = 1.0
        _FadeTime ("Fade Seconds", Float) = 0.35
        _LaneTile ("Lane Crack Tile (m)", Float) = 1.35
        _Age ("Age (s since slam)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-8" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "WreckIronGround"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CrackTex);
            TEXTURE2D(_DirtTex);
            TEXTURE2D(_CraterCrackTex);
            TEXTURE2D(_SplatTex);
            TEXTURE2D(_StarTex);
            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_linear_repeat);
            SAMPLER(sampler_linear_clamp);

            CBUFFER_START(UnityPerMaterial)
                half4 _DarkColor;
                half _DarkAlpha;
                half _CrackDark;
                half4 _GlowColor;
                half4 _HotColor;
                half _GlowBase;
                half _GlowHot;
                half _GlowBlur;
                half _SpotGlow;
                half _CraterGlow;
                half _CraterCrackGlow;
                half _LaneEdgeGlow;
                half _CrackReach;
                float _SplatScale;
                float _StarScale;
                float _LinkA;
                float _LinkC;
                float _LinkTube;
                float _Cool;
                float _Hold;
                float _FadeTime;
                float _LaneTile;
                float _Age;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv0 = input.uv0;
                output.uv1 = input.uv1;
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half Line(float x, float width) { return exp(-(x * x) / max(1e-5, width * width)); }

            // Мягкий круглый обрез квадрата фактуры: куски пятна у края квадрата не режутся прямой линией.
            half Round(float2 uv, half from) { return 1.0h - smoothstep(from, 0.5h, length(uv - 0.5)); }

            // Поворот фактуры вокруг середины на долю оборота и масштаб (меньше 1 — фактура крупнее).
            float2 Turn(float2 uv, float turns, float scale)
            {
                float s, c;
                sincos(turns * 6.2831853, s, c);
                float2 p = uv - 0.5;
                return float2(c * p.x - s * p.y, s * p.x + c * p.y) * scale + 0.5;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float kind = input.uv1.y;
                // zw — метры поперёк/вдоль у всех кусков: сеть трещин и шум идут без шва.
                float2 metres = input.uv0.zw;
                half noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_linear_repeat, metres * 0.45).r;
                half fine = SAMPLE_TEXTURE2D(_NoiseTex, sampler_linear_repeat, metres * 1.7 + 0.37).r;
                // Рваный фронт: кусок открывается чуть раньше/позже по шуму, не ровной линией.
                float local = _Age - input.uv1.x + (noise - 0.5) * 0.035;
                clip(local);
                half heat = exp(-local / max(0.01, _Cool));
                half fade = 1.0h - smoothstep(_Hold, _Hold + _FadeTime, local);
                half glowFade = 1.0h - smoothstep(_Hold * 0.55, _Hold + _FadeTime * 0.5, local);

                // Сеть трещин разбитой земли (Crack4) — два поворота, чтобы не читался повтор.
                float2 tile = metres / max(0.1, _LaneTile);
                float2 uvA = tile + float2(0.5, 0.0);
                float2 uvB = float2(-tile.y, tile.x) * 0.77 + float2(0.31, 0.53);
                half crack = saturate(max(SAMPLE_TEXTURE2D(_CrackTex, sampler_linear_repeat, uvA).r,
                                          SAMPLE_TEXTURE2D(_CrackTex, sampler_linear_repeat, uvB).r) * 1.35h - 0.08h);
                half seep = saturate(max(SAMPLE_TEXTURE2D_BIAS(_CrackTex, sampler_linear_repeat, uvA, _GlowBlur).r,
                                         SAMPLE_TEXTURE2D_BIAS(_CrackTex, sampler_linear_repeat, uvB, _GlowBlur).r) * 2.2h);
                seep *= seep;

                half dirt = 0.0h, pool = 0.0h, lines = 0.0h, net = 1.0h;
                half glowGain = 1.0h;
                if (kind < 0.5)
                {
                    // Полоса: край — шум камней (g) и шум CFXR, концы рваные (r); свет из трещин — у оси.
                    half across = abs(input.uv0.x) / max(0.1, input.uv1.z);
                    half edgeAt = input.color.g + (fine - 0.5h) * 0.24h + (noise - 0.5h) * 0.14h;
                    half fill = 1.0h - smoothstep(edgeAt - 0.22h, edgeAt + 0.02h, across);
                    fill *= saturate((input.color.r - fine * 0.65h) * 3.5h);
                    dirt = fill * saturate(0.5h + noise * 0.8h);
                    // Трещины выбегают за край ветками разной длины (шум), гаснут к краю сетки.
                    half beyond = across - edgeAt;
                    half reach = (1.0h - smoothstep(0.0h, _CrackReach * (0.35h + 0.9h * noise), beyond)) * 0.9h;
                    net = max(saturate(fill * 1.6h), reach * saturate(input.color.r * 2.0h));
                    half axis = 1.0h - smoothstep(0.3h, 0.8h, across);
                    glowGain = (_LaneEdgeGlow + (1.0h - _LaneEdgeGlow) * axis) * saturate(fill * 1.6h);
                }
                else if (kind < 1.5)
                {
                    // Круг: тёмная воронка Crater40 + рваное пятно Crater2 (без ровного диска), звезда трещин
                    // Hovl Crack выбегает за кромку (тёмная), светятся трещины Crater19 из-под якоря.
                    float2 uv = input.uv0.xy;
                    float r = length(uv - 0.5) * 2.0;
                    half crater = smoothstep(0.35h, 0.85h, SAMPLE_TEXTURE2D(_DirtTex, sampler_linear_clamp, uv).a) * Round(uv, 0.46h);
                    float2 su = Turn(uv, input.uv1.z, _SplatScale);
                    half4 splat = SAMPLE_TEXTURE2D(_SplatTex, sampler_linear_clamp, su);
                    half splatMask = Round(su, 0.4h);
                    // Пятно пёстрое по шуму: разбитая земля, а не клякса тушью.
                    dirt = max(crater * 0.85h, smoothstep(0.2h, 0.65h, splat.a) * splatMask * lerp(0.55h, 1.0h, fine));
                    float2 tu = Turn(uv, input.uv1.z + 0.37, _StarScale);
                    half star = SAMPLE_TEXTURE2D(_StarTex, sampler_linear_clamp, tu).a * Round(tu, 0.42h);
                    half radial = SAMPLE_TEXTURE2D(_CraterCrackTex, sampler_linear_clamp, uv).r * Round(uv, 0.45h);
                    half radialSeep = SAMPLE_TEXTURE2D_BIAS(_CraterCrackTex, sampler_linear_clamp, uv, _GlowBlur * 0.8).r * Round(uv, 0.45h);
                    half glowLines = smoothstep(0.25h, 0.6h, radial);
                    lines = max(glowLines, star * 0.85h);
                    half centre = 1.0h - smoothstep(0.0h, 0.45h, r);
                    pool = centre * centre * (0.55h + 0.45h * noise) * _CraterGlow
                         + (glowLines * 0.9h + saturate(radialSeep * 1.6h) * 0.6h) * _CraterCrackGlow;
                    net = saturate(dirt * 1.5h);
                    glowGain = (0.35h + 0.65h * (1.0h - smoothstep(0.3h, 1.0h, r))) * net;
                }
                else if (kind < 2.5)
                {
                    // Паз под звеном: осевая линия звена (полукружия A на ±C), паз шириной в прут,
                    // свет — в дыре звена и тонкой линией по внутренней кромке; ребром — брусок без дыры.
                    float2 p = abs(input.uv0.xy);
                    bool edgeLink = input.uv1.z > 0.5;
                    float s = edgeLink
                        ? length(float2(p.x, max(p.y - (_LinkC + _LinkA), 0.0)))
                        : length(float2(p.x, max(p.y - _LinkC, 0.0))) - _LinkA;
                    float d = abs(s);
                    half groove = 1.0h - smoothstep(_LinkTube * 1.05, _LinkTube * 2.1, d);
                    half hole = edgeLink ? 0.0h : smoothstep(-_LinkTube * 0.9, -_LinkTube * 1.7, s);
                    half innerRim = edgeLink ? 0.0h : Line(s + _LinkTube * 1.08, _LinkTube * 0.4);
                    half sideRim = Line(d - _LinkTube * 1.15, _LinkTube * 0.35) * (edgeLink ? 0.8h : 0.3h);
                    dirt = saturate(groove * 0.95h + hole * 0.75h);
                    lines = saturate(innerRim + sideRim);
                    pool = (hole * (0.06h + 0.75h * crack + 0.3h * seep) + innerRim * 1.2h + sideRim) * _SpotGlow;
                    glowGain = 0.6h + 0.4h * groove;
                    net = 0.0h;
                }
                else
                {
                    // Рваное пятно разбитой земли (Hovl Crater2, альфа с порогом): тело и отлетевшие куски; свет — слабый.
                    half4 splat = SAMPLE_TEXTURE2D(_SplatTex, sampler_linear_clamp, input.uv0.xy);
                    dirt = smoothstep(0.2h, 0.65h, splat.a) * Round(input.uv0.xy, 0.42h) * lerp(0.5h, 1.0h, fine);
                    net = saturate(dirt * 1.5h);
                    glowGain = 0.3h * net;
                }

                half edge = input.color.a;
                half darkAlpha = saturate(dirt) * _DarkAlpha * fade * edge;
                half crackAlpha = saturate(max(crack * net, lines)) * _CrackDark * fade * edge;
                half alpha = saturate(max(darkAlpha, crackAlpha));
                half3 rgb = _DarkColor.rgb * alpha;

                // Жар — бледно-голубой #9CD8FF в первые сотые доли, дальше насыщенный холодный голубой.
                half3 glowColor = lerp(_GlowColor.rgb, _HotColor.rgb, saturate(heat * 0.6h + lines * 0.1h));
                half power = input.uv1.w * edge;
                half steady = _GlowBase * glowFade + _GlowHot * heat;
                half glow = seep * glowGain * steady * power;
                glow += pool * (0.55h * glowFade + 0.4h * heat) * power;
                rgb += glowColor * glow;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
