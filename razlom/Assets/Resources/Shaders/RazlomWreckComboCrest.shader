Shader "Razlom/Wreck Combo Crest"
{
    // СТОЯЧИЙ ГРЕБЕНЬ ВЫПАДА КРУШЕНИЯ — «холодное железо» на стеке серии сабли (06.10 поздно,
    // Editor/PelagWreckComboLungeVfxSetup; лента строится кадр за кадром в PelagVfxController.WreckComboLunge).
    //
    // Те же приёмы, что «Razlom/Wreck Combo Wave» (махи) и «Razlom/Sabre Foam Wave» (сабля; их шейдеры не трогаются),
    // переложенные на ленту вдоль полосы Sim, поднятую к экрану (как стоячая волна добивающего сабли):
    //  • u — вдоль полосы, метры / 4 от героя (рисунок не тянется с длиной полосы), v — от земли (0) к гребню (1);
    //  • TEXCOORD0.z — возраст ТОЧКИ ленты (с тех пор, как через неё прошёл фронт Sim), TEXCOORD0.w — возраст удара:
    //    гребень у фронта молодой и горячий, позади — рвётся гранями и гаснет; штрихи и блик текут по общему времени;
    //  • полосы: глубина у земли, кобальт, светлая сталь, горячая почти белая кромка по рваному верху (зубцы-занозы,
    //    загнуты назад, чернильный обвод постоянной ширины в пикселях); у фронта кромка шире — гребень раскалён;
    //  • грани пака «cfxr sword trail noise ice» — плоские ячейки стали и угловатый распад, не пузырьки.
    // Смешивание премультиплированное, как у волны маха.
    Properties
    {
        _Deep ("Deep (ground)", Color) = (.04,.12,.29,1)
        _Mid ("Cobalt", Color) = (.18,.44,.88,1)
        _Light ("Light steel", Color) = (.56,.77,1,1)
        _Hot ("Hot rim (near-white cold)", Color) = (1.10,1.18,1.30,1)
        _HotShade ("Hot rim facet shade", Color) = (.70,.84,1,1)
        _Outline ("Ink outline", Color) = (.02,.04,.12,.94)
        _FacetTex ("Facet noise (r)", 2D) = "gray" {}
        _FacetScale ("Facet scale (fine u, fine v, coarse u, coarse v)", Vector) = (1.4,2.2,.9,.8)
        _SliverScale ("Slivers (u scale, v scale, threshold, flow x)", Vector) = (.55,4.5,.80,1.6)
        _Bands ("Bands (deep end, cobalt end, hot from, hot rag)", Vector) = (.20,.46,.72,.14)
        _EdgeRag ("Top edge rag", Range(0,.5)) = .14
        _EdgeTeeth ("Top edge teeth depth", Range(0,.5)) = .18
        _TeethCount ("Teeth per u", Float) = 14
        _FrontHot ("Extra hot rim at the front", Range(0,.6)) = .28
        _FrontHotSeconds ("Front stays hot (s)", Float) = .07
        _OutlinePx ("Outline width, px", Range(0,6)) = 2.4
        _ErodeGain ("Erosion edge in width units", Float) = 1.6
        _ErodeFrom ("Erode from (local s)", Float) = .08
        _ErodeTo ("Erode to (local s)", Float) = .26
        _ErodeBias ("Erosion field bias", Range(-.3,.3)) = .07
        _FadeFrom ("Fade from (local s)", Float) = .20
        _FadeTo ("Fade to (local s)", Float) = .30
        _Streaks ("Metal streaks", Range(0,1)) = .8
        _Sheen ("Travelling sheen", Range(0,1)) = .6
        _SheenRepeat ("Sheen stripes per u", Float) = 1.6
        _SheenRate ("Sheen stripes per second", Float) = 5
        _SheenWidth ("Sheen width (stripe fraction)", Range(.01,.5)) = .05
        _Glow ("Hot rim glow", Range(0,1)) = .32
        _Opacity ("Opacity", Range(0,1)) = 1
        _FlowSpeed ("Flow speed (u/s)", Float) = 1.4
        _CameraPush ("Push toward camera (m)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+40" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Wreck Combo Crest"
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
            half4 _Deep, _Mid, _Light, _Hot, _HotShade, _Outline;
            float4 _FacetTex_ST, _FacetScale, _SliverScale, _Bands;
            float _EdgeRag, _EdgeTeeth, _TeethCount, _FrontHot, _FrontHotSeconds, _OutlinePx, _ErodeGain;
            float _ErodeFrom, _ErodeTo, _ErodeBias, _FadeFrom, _FadeTo, _Streaks, _Sheen, _SheenRepeat, _SheenRate, _SheenWidth;
            float _Glow, _Opacity, _FlowSpeed, _CameraPush;
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
                float u = i.uv.x, v = i.uv.y;
                float local = max(0, i.uv.z), global = max(0, i.uv.w);
                float erodeP = 1.05 * smoothstep(_ErodeFrom, max(_ErodeFrom + .01, _ErodeTo), local);
                float fade = _Opacity * (1 - smoothstep(_FadeFrom, max(_FadeFrom + .01, _FadeTo), local));
                float flow = global * _FlowSpeed;
                float young = 1 - smoothstep(0, max(.005, _FrontHotSeconds), local);

                // Грани стали бегут вперёд по полосе (за фронтом); крупные — медленнее (край, распад).
                float fine = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, float2((u - flow) * _FacetScale.x, v * _FacetScale.y)).r;
                float coarse = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex,
                    float2((u - flow * .5) * _FacetScale.z + .37, v * _FacetScale.w + .21)).r;
                float sliver = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex,
                    float2((u - flow * _SliverScale.w) * _SliverScale.x + .61, v * _SliverScale.y + .13)).r;

                // Силуэт: низ у земли ровный (уходит под землю), верх — рваный гранями и зубцами-занозами, загнутыми
                // назад (осколки срываются против хода); у фронта зубцы глубже — гребень рвётся вперёд.
                float tu = (u - flow * .8) * _TeethCount;
                float toothCell = floor(tu);
                float toothDepth = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, float2(toothCell * .0731 + .19, .43)).r;
                float tooth = (1 - frac(tu)) * (.25 + .75 * toothDepth) * (1 + .5 * young);
                float dOuter = (1 - _EdgeRag * (1 - coarse) - _EdgeTeeth * tooth) - v;
                float dInner = v;
                float dErode = (coarse - _ErodeBias - erodeP) * _ErodeGain;
                float d = min(min(dInner, dOuter), dErode);
                float px = d / max(fwidth(d), 1e-5);
                float shape = saturate(px + .5);
                float body = saturate(px - _OutlinePx + .5);

                // Полосы: глубина у земли → кобальт → светлая сталь; границы ступенятся по граням.
                float vb = saturate(v + (fine - .5) * .06);
                float aa = max(fwidth(vb) * 1.2, .003);
                half3 col = _Deep.rgb;
                col = lerp(col, _Mid.rgb, Crisp(_Bands.x, vb, aa));
                col = lerp(col, _Light.rgb, Crisp(_Bands.y, vb, aa));
                col *= lerp(.90, 1.05, fine);

                // Металлическая «протяжка» — длинные яркие занозы, текут вперёд быстрее тела.
                float sa = max(fwidth(sliver) * .9, .002);
                float streak = Crisp(_SliverScale.z, sliver, sa) * Crisp(_Bands.x * .7, vb, aa) * (1 - Crisp(_Bands.z, vb, aa));
                col = lerp(col, lerp(_Light.rgb, _Hot.rgb, .6), streak * _Streaks);

                // Блик: тонкие косые полосы бегут вперёд по стали (движение внутри стоячего гребня).
                float sheenS = frac((u + (v - .5) * .10) * _SheenRepeat - global * _SheenRate);
                float sw = max(fwidth(sheenS) * 1.5, .004);
                float sheen = (1 - Crisp(_SheenWidth, min(sheenS, 1 - sheenS), sw)) * Crisp(_Bands.y - .1, vb, aa);
                col = lerp(col, _Hot.rgb, sheen * _Sheen);

                // Горячая кромка по верху, у фронта шире (раскалённый гребень), рвётся занозами, два тона по граням.
                float hotFrom = _Bands.z - (coarse - .5) * _Bands.w * 2 - young * _FrontHot - _EdgeTeeth * tooth * .8;
                float af = max(fwidth(v) * 1.2, .003);
                float hot = Crisp(hotFrom, v, af);
                half3 hotCol = lerp(_HotShade.rgb, _Hot.rgb, step(.40, fine));
                col = lerp(col, hotCol, hot);

                col = lerp(_Outline.rgb, col, body);
                float alpha = shape * lerp(_Outline.a, 1, body) * fade * i.color.a;
                half3 glow = hotCol * (hot * (1 + young) + sheen * .5) * body * shape * fade * _Glow;
                half3 rgb = (col * alpha + glow) * i.color.rgb;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
