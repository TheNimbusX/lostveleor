Shader "Razlom/Wreck Ghost Anchor"
{
    // ПРИЗРАЧНЫЙ ЯКОРЬ Крушения, v2 (07.10 после отзыва «обводка херовая»; Editor/PelagWreckComboVfxSetup.Ghost;
    // целевой кадр v4-form-ghost-anchor.png). Тело настоящего 3D якоря Пелага (меш Tripo + призрачная цепь витком вокруг
    // ствола, копия Geometry/WreckGhostAnchor: низ в 0, высота 1, сглаженные нормали в TEXCOORD1 для ореола).
    // СВЕТИТСЯ, а не наклейка: чернил нет вовсе.
    //  • внутри — лавандовое «стекло» (_Inner/_Core) с гранями-кристаллами (клетки пака «noise ice» — три тона и тонкие
    //    светлые линии по их границам), объём от постоянного света (свет/тень двумя тонами), чуть видна земля сквозь;
    //  • полосы фиолетового #8E7BFF текут ВНИЗ по высоте якоря (_Age);
    //  • край силуэта — светлая кромка: бледно-фиолетовая полоса, у самого края белая HDR (френель с порогом, рвётся
    //    гранями) — даёт лёгкий bloom арены; кромка и полосы непрозрачны, силуэт чёткий;
    //  • проявление (_Appear) и распад (_Dissolve, снизу вверх) режутся порогом по граням пака: у края среза — белая
    //    горячая полоса (не чернила);
    //  • вспышка всего тела в миг удара (_Flash).
    // Ореол снаружи силуэта — «Razlom/Wreck Ghost Anchor Glow» (две оболочки по сглаженным нормалям; это тело пишет
    // трафарет 200, ореол рисуется только вне силуэта).
    Properties
    {
        _Inner ("Inside, shadow side (lavender)", Color) = (.42,.36,.92,1)
        _Core ("Inside, lit side (#D9CCFF)", Color) = (.69,.60,1,1)
        _Band ("Flowing bands (#8E7BFF)", Color) = (.27,.20,1,1)
        _Rim ("Rim band (pale violet)", Color) = (.80,.74,1.15,1)
        _Hot ("Hot rim / cut edge / flash (HDR white)", Color) = (1.7,1.62,2.1,1)
        _FacetTex ("Facet cells (r, «noise ice»)", 2D) = "gray" {}
        _NoiseScale ("Noise scale (facets xy, cut xy) per mesh unit", Vector) = (.65,.9,1.1,1.4)
        _LightDir ("Volume light dir (world)", Vector) = (-.45,.75,-.48,0)
        _LitStep ("Lit step (n.l)", Range(0,1)) = .55
        _FacetTones ("Facet tones (dark gain, light gain, light from, dark below)", Vector) = (.80,1.18,.62,.30)
        _FacetLines ("Facet line strength", Range(0,1)) = .75
        _FacetLineWidth ("Facet line threshold (derivative)", Range(0,1)) = .18
        _BandFreq ("Bands per anchor height", Float) = 3.5
        _BandWidth ("Band width (0..1 of period)", Range(0,1)) = .24
        _BandSpeed ("Band flow (periods/s, down)", Float) = 1.6
        _BandWarp ("Band warp by facets", Range(0,1)) = .18
        _RimFrom ("Pale rim from (fresnel)", Range(0,1)) = .48
        _HotFrom ("White rim from (fresnel)", Range(0,1)) = .66
        _InnerAlpha ("Inner opacity", Range(0,1)) = .78
        _EdgeHot ("Cut edge: hot width", Range(0,.3)) = .07
        _Age ("Age (s, controller)", Float) = 0
        _Appear ("Appear 0..1 (controller)", Range(0,1)) = 1
        _Dissolve ("Dissolve 0..1, bottom first (controller)", Range(0,1)) = 0
        _Flash ("Impact flash 0..1 (controller)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+36" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Wreck Ghost Anchor"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Back
            Stencil { Ref 200 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
            half4 _Inner, _Core, _Band, _Rim, _Hot;
            float4 _FacetTex_ST, _NoiseScale, _LightDir, _FacetTones;
            float _LitStep, _FacetLines, _FacetLineWidth, _BandFreq, _BandWidth, _BandSpeed, _BandWarp, _RimFrom, _HotFrom;
            float _InnerAlpha, _EdgeHot, _Age, _Appear, _Dissolve, _Flash;
            CBUFFER_END
            TEXTURE2D(_FacetTex); SAMPLER(sampler_FacetTex);

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            float Crisp(float edge, float x, float aa) { return smoothstep(edge - aa, edge + aa, x); }

            // Поле среза (общее с ореолом): > 0 — тело есть. Распад снизу вверх по граням, проявление — по граням.
            float CutField(float3 p)
            {
                float coarse = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, p.xy * _NoiseScale.zw + float2(.37, .21)).r;
                float h = saturate(p.y);
                float dissolve = (h * .62 + coarse * .38) - (_Dissolve * 1.12 - .06);
                float appear = coarse - (1.06 - _Appear * 1.12);
                return min(dissolve, appear);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float cut = CutField(i.positionOS);
                clip(cut);
                // Грани: клетки пака в плоскости, повёрнутой к ширине якоря (xy) и глубине (zy) — по нормали.
                float3 N = normalize(i.normalWS);
                float2 fuv = (abs(N.x) > abs(N.z) ? i.positionOS.zy : i.positionOS.xy) * _NoiseScale.xy + float2(.11, .53);
                float fine = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, fuv).r;

                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 L = normalize(_LightDir.xyz);

                // Объём: светлая и теневая сторона двумя тонами, грани — три тона поверх.
                float ndl = dot(N, L) * .5 + .5;
                float aa = max(fwidth(ndl) * 1.2, .004);
                half3 col = lerp(_Inner.rgb, _Core.rgb, Crisp(_LitStep, ndl, aa));
                float facetLight = step(_FacetTones.z, fine), facetDark = step(fine, _FacetTones.w);
                col *= lerp(1, _FacetTones.y, facetLight) * lerp(1, _FacetTones.x, facetDark);

                // Линии граней: границы клеток (скачок значения) — тонкие светлые черты, как сколы кристалла.
                float facetLine = Crisp(_FacetLineWidth, saturate(fwidth(fine) * 6), .04) * _FacetLines;
                col = lerp(col, _Rim.rgb, facetLine);

                // Полосы фиолетового текут вниз по высоте якоря.
                float b = i.positionOS.y * _BandFreq + _Age * _BandSpeed + (fine - .5) * _BandWarp;
                float fb = frac(b);
                float ab = max(fwidth(b) * 1.2, .004);
                float band = Crisp(1 - _BandWidth, fb, ab) * (1 - Crisp(.985, fb, ab));
                col = lerp(col, _Band.rgb, band * .85);

                // Светлая кромка силуэта: бледная полоса, у самого края — белая (HDR).
                float fres = 1 - saturate(abs(dot(N, V)));
                float rimF = fres + (fine - .5) * .10;
                float ar = max(fwidth(rimF) * 1.2, .004);
                float rim = Crisp(_RimFrom, rimF, ar);
                float hot = Crisp(_HotFrom, rimF, ar);
                col = lerp(col, _Rim.rgb, rim);
                col = lerp(col, _Hot.rgb, hot);

                // Край среза (проявление, распад): белая горячая полоса.
                float ac = max(fwidth(cut) * 1.2, .002);
                float hotEdge = 1 - Crisp(_EdgeHot, cut, ac);
                col = lerp(col, _Hot.rgb, hotEdge);

                // Вспышка удара — к светлой кромке, не к белому HDR (раунд 4: белый «блин» на 3–4 кадра).
                col = lerp(col, _Rim.rgb, saturate(_Flash) * .85);
                float alpha = lerp(_InnerAlpha, 1, saturate(max(max(rim, band * .6), max(hotEdge, facetLine * .5))));
                alpha = max(alpha, saturate(_Flash));
                return half4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
