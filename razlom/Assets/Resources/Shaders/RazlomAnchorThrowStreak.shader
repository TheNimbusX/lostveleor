Shader "Razlom/Anchor Throw Streak"
{
    // СТРУНА БРОСКА ЯКОРЯ — «холодное железо» якорной семьи (07–08.10, Editor/PelagWreckComboVfxSetup.ThrowIron; лента строится кадр
    // за кадром в Game.View/PelagAnchorThrowIronStrip по хвату и кольцу рига). Целевые кадры — base-hit-v1 / base-yank-v1.
    //
    // Приёмы — копия «Razlom/Wreck Combo Crest» (шейдер Крушения не трогается), переложенные на ленту ВДОЛЬ ЦЕПИ, повёрнутую
    // к экрану (ось v поперёк, симметрично: |v| = 0 — ось цепи, 1 — край):
    //  • u — метры от кольца головы к хвату / 4 (рисунок едет вместе с головой: в полёте цепь «выдаётся», на возврате
    //    выбирается к руке); TEXCOORD0.z — возраст распада (после ловли), TEXCOORD0.w — секунды с выпуска;
    //  • вершинный цвет: r — жар (ширина бело-горячей сердцевины), g — шевроны рывка (бегут по цепи к руке),
    //    b — импульс натяга (полоса света бежит от головы к руке), a — непрозрачность;
    //  • полосы от оси к краю: горячая почти белая сердцевина, светлая сталь, кобальт, глубина у края; грани пака
    //    «cfxr sword trail noise ice» (плоские ячейки стали, рваный край зубцами назад), протяжка-занозы, бегущий блик;
    //  • КРАЙ — светящийся тёмный кобальт постоянной ширины в пикселях, НЕ чёрные чернила (урок Призрачного якоря 07.10).
    // Ореол (_HaloMode 1) — тот же шейдер шире и без сердцевины, глубокий кобальт полупрозрачно: свет вокруг цепи.
    // Смешивание премультиплированное; _CameraPush < 0 — лента за цепью рига (звенья видны поверх света, как на кадре).
    Properties
    {
        _Deep ("Deep (edge)", Color) = (.04,.12,.29,1)
        _Mid ("Cobalt", Color) = (.18,.44,.88,1)
        _Light ("Light steel", Color) = (.56,.77,1,1)
        _Hot ("Hot core (near-white cold)", Color) = (.92,1.06,1.40,1)
        _HotShade ("Hot core facet shade", Color) = (.62,.82,1.15,1)
        _Edge ("Glowing edge (not ink)", Color) = (.07,.20,.62,.92)
        _FacetTex ("Facet noise (r)", 2D) = "gray" {}
        _FacetScale ("Facet scale (fine u, fine v, coarse u, coarse v)", Vector) = (1.4,1.2,.9,.6)
        _SliverScale ("Slivers (u scale, v scale, threshold, flow x)", Vector) = (.55,3.0,.80,1.6)
        _Bands ("Bands |v| (core at heat 1, light end, cobalt end, core at heat 0)", Vector) = (.24,.46,.78,.08)
        _EdgeRag ("Edge rag", Range(0,.5)) = .16
        _EdgeTeeth ("Edge teeth depth", Range(0,.5)) = .18
        _TeethCount ("Teeth per u", Float) = 22
        _EdgePx ("Edge width, px", Range(0,6)) = 1.8
        _ErodeGain ("Erosion edge in width units", Float) = 1.6
        _ErodeFrom ("Erode from (s)", Float) = .02
        _ErodeTo ("Erode to (s)", Float) = .20
        _ErodeBias ("Erosion field bias", Range(-.3,.3)) = .05
        _FadeFrom ("Fade from (s)", Float) = .14
        _FadeTo ("Fade to (s)", Float) = .24
        _Streaks ("Metal streaks", Range(0,1)) = .7
        _Sheen ("Travelling sheen", Range(0,1)) = .6
        _SheenRepeat ("Sheen stripes per u", Float) = 2.2
        _SheenRate ("Sheen stripes per second (to the head)", Float) = 6
        _SheenWidth ("Sheen width (stripe fraction)", Range(.01,.5)) = .06
        _Chevrons ("Chevrons per u", Float) = 6
        _ChevronSlope ("Chevron arm slope", Float) = .55
        _ChevronRate ("Chevrons per second (to the hand)", Float) = 9
        _ChevronWidth ("Chevron width (stripe fraction)", Range(.01,.5)) = .12
        _PulseGain ("Tension pulse brightness", Range(0,2)) = .9
        _Glow ("Hot glow", Range(0,1)) = .35
        _Opacity ("Opacity", Range(0,1)) = 1
        _HaloMode ("Halo (no core, flat cobalt)", Float) = 0
        _FlowSpeed ("Flow speed (u/s)", Float) = 1.2
        _CameraPush ("Push toward camera (m)", Float) = -.10
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+38" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Anchor Throw Streak"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float4 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 uv : TEXCOORD0; half4 color : COLOR; };

            CBUFFER_START(UnityPerMaterial)
            half4 _Deep, _Mid, _Light, _Hot, _HotShade, _Edge;
            float4 _FacetTex_ST, _FacetScale, _SliverScale, _Bands;
            float _EdgeRag, _EdgeTeeth, _TeethCount, _EdgePx, _ErodeGain, _ErodeFrom, _ErodeTo, _ErodeBias, _FadeFrom, _FadeTo;
            float _Streaks, _Sheen, _SheenRepeat, _SheenRate, _SheenWidth, _Chevrons, _ChevronSlope, _ChevronRate, _ChevronWidth;
            float _PulseGain, _Glow, _Opacity, _HaloMode, _FlowSpeed, _CameraPush;
            CBUFFER_END
            TEXTURE2D(_FacetTex); SAMPLER(sampler_FacetTex);

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                positionWS += GetWorldSpaceNormalizeViewDir(positionWS) * _CameraPush;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float Crisp(float edge, float x, float aa) { return smoothstep(edge - aa, edge + aa, x); }

            half4 Frag(Varyings i) : SV_Target
            {
                float u = i.uv.x, v = i.uv.y, av = abs(v);
                float erodeAge = max(0, i.uv.z), global = max(0, i.uv.w);
                float heat = i.color.r, chev = i.color.g, pulse = i.color.b;
                float erodeP = 1.05 * smoothstep(_ErodeFrom, max(_ErodeFrom + .01, _ErodeTo), erodeAge);
                float fade = _Opacity * (1 - smoothstep(_FadeFrom, max(_FadeFrom + .01, _FadeTo), erodeAge));
                float flow = global * _FlowSpeed;

                // Грани стали: мелкие текут к голове (энергия толкает якорь), крупные — медленнее (край, распад).
                float fine = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, float2((u + flow) * _FacetScale.x, v * _FacetScale.y)).r;
                float coarse = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex,
                    float2((u + flow * .4) * _FacetScale.z + .37, v * _FacetScale.w + .21)).r;
                float sliver = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex,
                    float2((u + flow * _SliverScale.w) * _SliverScale.x + .61, v * _SliverScale.y + .13)).r;

                // Силуэт: рваный край гранями и зубцами-занозами, загнутыми к руке (свет срывается назад по ходу полёта).
                float tu = u * _TeethCount + (v > 0 ? .5 : 0);
                float toothCell = floor(tu);
                float toothDepth = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, float2(toothCell * .0731 + .19 + (v > 0 ? .5 : 0), .43)).r;
                float tooth = frac(tu) * (.25 + .75 * toothDepth);
                float dOuter = (1 - _EdgeRag * (1 - coarse) - _EdgeTeeth * tooth) - av;
                float dErode = (coarse - _ErodeBias - erodeP) * _ErodeGain;
                float d = min(dOuter, dErode);
                float px = d / max(fwidth(d), 1e-5);
                float shape = saturate(px + .5);
                float body = saturate(px - _EdgePx + .5);

                // Полосы от оси к краю: сердцевина (ширина — жар), сталь, кобальт, глубина; границы ступенятся по граням.
                float vb = saturate(av + (fine - .5) * .08);
                float aa = max(fwidth(vb) * 1.2, .003);
                float coreW = lerp(_Bands.w, _Bands.x, saturate(heat)) + pulse * .18;
                half3 col = _Deep.rgb;
                col = lerp(col, _Mid.rgb, 1 - Crisp(_Bands.z, vb, aa));
                col = lerp(col, _Light.rgb, 1 - Crisp(_Bands.y, vb, aa));
                col *= lerp(.88, 1.06, fine);

                // Протяжка — длинные яркие занозы стали вдоль цепи.
                float sa = max(fwidth(sliver) * .9, .002);
                float streak = Crisp(_SliverScale.z, sliver, sa) * (1 - Crisp(_Bands.z, vb, aa));
                col = lerp(col, lerp(_Light.rgb, _Hot.rgb, .55), streak * _Streaks * (1 - _HaloMode));

                // Блик: косые полосы бегут к голове (полёт); на рывке гаснут — бегут шевроны.
                float sheenS = frac((u + av * .12) * _SheenRepeat + global * _SheenRate);
                float sw = max(fwidth(sheenS) * 1.5, .004);
                float sheen = (1 - Crisp(_SheenWidth, min(sheenS, 1 - sheenS), sw)) * (1 - Crisp(_Bands.z, vb, aa)) * (1 - chev);
                col = lerp(col, _Hot.rgb, sheen * _Sheen * (1 - _HaloMode));

                // Шевроны рывка «>» к руке: остриё на оси, рукава назад к голове.
                float cs = frac(u * _Chevrons - av * _ChevronSlope - global * _ChevronRate);
                float cw = max(fwidth(cs) * 1.5, .004);
                float chevron = (1 - Crisp(_ChevronWidth, min(cs, 1 - cs), cw)) * (1 - Crisp(_Bands.z + .08, vb, aa)) * chev;
                col = lerp(col, lerp(_Light.rgb, _Hot.rgb, .8), chevron * (1 - _HaloMode));

                // Горячая сердцевина по оси (два тона по граням), в импульс натяга шире и ярче.
                float hot = (1 - Crisp(coreW, vb, aa)) * (1 - _HaloMode);
                half3 hotCol = lerp(_HotShade.rgb, _Hot.rgb, step(.38, fine));
                col = lerp(col, hotCol, hot);

                col = lerp(_Edge.rgb, col, body);
                float alpha = shape * lerp(_Edge.a, 1, body) * fade * i.color.a;
                half3 glow = _Hot.rgb * (hot * (1 + pulse * _PulseGain) + sheen * .5 * _Sheen + chevron * .6 + pulse * .35 * (1 - vb))
                    * body * shape * fade * _Glow * i.color.a;
                return half4(col * alpha + glow, alpha);
            }
            ENDHLSL
        }
    }
}
