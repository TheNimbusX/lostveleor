Shader "Razlom/Wreck Ninth Crest"
{
    // ДЕВЯТЫЙ ВАЛ — гребни веера выпада (07.10, Editor/PelagWreckComboVfxSetup.Ninth; ленты строит
    // Game.View/PelagWreckNinthCrest кадр за кадром). Приёмы гребня базы «Razlom/Wreck Combo Crest» (сам он не трогается):
    // лента вдоль полосы Sim, поднятая к экрану, u — метры / 4, v — низ (0) / верх (1), TEXCOORD0.z — возраст точки ленты
    // (с тех пор, как через неё прошёл фронт Sim), .w — возраст удара; полосы, грани пака «noise ice», протяжка, распад
    // гранями и порог. Своё — силуэт и свет, как на целевом кадре v4-form-ninthwave:
    //  • верх — ШИПЫ: крупные редкие разной высоты и ширины (иногда игла) и мелкие частые, клонятся вперёд по ходу вала;
    //  • свет внутри, а не по краю: тело индиго от глубины у земли к светлому фиолетовому, у верха тела — горячая
    //    бело-фиолетовая полоса (середина вала), из неё идут шипы со светлой сердцевиной; край шипа — индиго;
    //  • контур — тонкий тёмно-индиговый (не чёрный: владелец 07.10 «обводка херовая»).
    // Смешивание премультиплированное, как у гребня базы.
    Properties
    {
        _Deep ("Deep (ground)", Color) = (.012,.007,.15,1)
        _Mid ("Indigo", Color) = (.07,.05,.63,1)
        _Light ("Light violet", Color) = (.31,.26,1,1)
        _Hot ("Hot white-violet", Color) = (.95,.92,1.30,1)
        _HotShade ("Hot facet shade", Color) = (.62,.57,1.12,1)
        _Outline ("Contour (dark indigo)", Color) = (.012,.004,.058,.92)
        _FacetTex ("Facet noise (r)", 2D) = "gray" {}
        _FacetScale ("Facet scale (fine u, fine v, coarse u, coarse v)", Vector) = (1.4,2.2,.9,.8)
        _SliverScale ("Slivers (u scale, v scale, threshold, flow x)", Vector) = (.55,4.5,.80,1.6)
        _Body ("Body top v, body rag", Vector) = (.50,.08,0,0)
        _Spikes ("Big spikes (per u, min, max, needle share)", Vector) = (9,.10,.40,.18)
        _SmallSpikes ("Small spikes (per u, min, max, width min)", Vector) = (23,.04,.15,.45)
        _SpikeLean ("Spike lean forward (u per v)", Float) = .05
        _SpikeSharp ("Spike side power", Float) = 1.7
        _Bands ("Bands (deep end, indigo end, hot from, -)", Vector) = (.24,.55,.80,0)
        _SpikeCore ("Spike core from (0..1 across)", Range(0,1)) = .64
        _FrontHot ("Front: taller spikes, wider hot", Range(0,.5)) = .14
        _FrontHotSeconds ("Front stays hot (s)", Float) = .08
        _OutlinePx ("Contour width, px", Range(0,6)) = 1.6
        _ErodeGain ("Erosion edge in width units", Float) = 1.6
        _ErodeFrom ("Erode from (local s)", Float) = .14
        _ErodeTo ("Erode to (local s)", Float) = .38
        _ErodeBias ("Erosion field bias", Range(-.3,.3)) = .02
        _FadeFrom ("Fade from (local s)", Float) = .32
        _FadeTo ("Fade to (local s)", Float) = .44
        _Streaks ("Light streaks", Range(0,1)) = .55
        _Sheen ("Travelling sheen", Range(0,1)) = .45
        _SheenRepeat ("Sheen stripes per u", Float) = 1.6
        _SheenRate ("Sheen stripes per second", Float) = 4.5
        _SheenWidth ("Sheen width (stripe fraction)", Range(.01,.5)) = .045
        _Glow ("Hot glow", Range(0,1)) = .34
        _Opacity ("Opacity", Range(0,1)) = 1
        _FlowSpeed ("Flow speed (u/s)", Float) = 1.4
        _CameraPush ("Push toward camera (m)", Float) = .2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+40" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Wreck Ninth Crest"
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
            float4 _FacetTex_ST, _FacetScale, _SliverScale, _Body, _Spikes, _SmallSpikes, _Bands;
            float _SpikeLean, _SpikeSharp, _SpikeCore, _FrontHot, _FrontHotSeconds, _OutlinePx, _ErodeGain;
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

            float Hash1(float n) { return frac(sin(n * 12.9898 + 4.1414) * 43758.5453); }

            // Один ряд шипов: клетка — один шип своей ширины, места в клетке и высоты; бока вогнуты (острые концы).
            float SpikeRow(float x, float hMin, float hMax, float wMin, float needle, float seed, out float core)
            {
                float cell = floor(x);
                float f = x - cell;
                float r1 = Hash1(cell + seed), r2 = Hash1(cell * 1.31 + seed + 7.1), r3 = Hash1(cell * 2.17 + seed + 3.3);
                float w = lerp(wMin, 1.0, r2);
                float c = .5 + (r3 - .5) * (1.0 - w);
                float p = saturate(1.0 - abs(f - c) / (w * .5));
                float h = lerp(hMin, hMax, r1 * r1) * (r1 > 1.0 - needle ? 1.3 : 1.0);
                core = p;
                return h * pow(p, _SpikeSharp);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float u = i.uv.x, v = i.uv.y;
                float local = max(0, i.uv.z), global = max(0, i.uv.w);
                float erodeP = 1.05 * smoothstep(_ErodeFrom, max(_ErodeFrom + .01, _ErodeTo), local);
                float fade = _Opacity * (1 - smoothstep(_FadeFrom, max(_FadeFrom + .01, _FadeTo), local));
                float flow = global * _FlowSpeed;
                float young = 1 - smoothstep(0, max(.005, _FrontHotSeconds), local);

                float fine = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, float2((u - flow) * _FacetScale.x, v * _FacetScale.y)).r;
                float coarse = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex,
                    float2((u - flow * .5) * _FacetScale.z + .37, v * _FacetScale.w + .21)).r;
                float sliver = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex,
                    float2((u - flow * _SliverScale.w) * _SliverScale.x + .61, v * _SliverScale.y + .13)).r;

                // Силуэт: тело до рваной линии, над ней шипы двух рядов, клонятся вперёд; у фронта шипы выше.
                float topBody = _Body.x - _Body.y * (1 - coarse);
                float above0 = max(0, v - topBody);
                float coreA, coreB;
                float sA = SpikeRow((u - _SpikeLean * above0) * _Spikes.x, _Spikes.y, _Spikes.z, .30, _Spikes.w, 0, coreA);
                float sB = SpikeRow((u - _SpikeLean * 1.3 * above0) * _SmallSpikes.x + .5, _SmallSpikes.y, _SmallSpikes.z,
                    _SmallSpikes.w, 0, 17.0, coreB);
                float grow = 1 + _FrontHot * 2 * young;
                float spike = max(sA, sB) * grow;
                float coreP = sA >= sB ? coreA : coreB;
                float top = min(.995, topBody + spike);
                float dOuter = top - v;
                float dInner = v;
                float dErode = (coarse - _ErodeBias - erodeP) * _ErodeGain;
                float d = min(min(dInner, dOuter), dErode);
                float px = d / max(fwidth(d), 1e-5);
                float shape = saturate(px + .5);
                float body = saturate(px - _OutlinePx + .5);

                // Тело: глубина у земли → индиго → светлый фиолетовый; доли — от верха тела, границы ступенятся по граням.
                float vb = saturate(v / max(topBody, .05) * .999 + (fine - .5) * .05);
                float aa = max(fwidth(vb) * 1.2, .003);
                half3 col = _Deep.rgb;
                col = lerp(col, _Mid.rgb, Crisp(_Bands.x, vb, aa));
                col = lerp(col, _Light.rgb, Crisp(_Bands.y, vb, aa));
                col *= lerp(.90, 1.06, fine);

                // Светлые занозы текут вперёд по телу (движение внутри стоящего вала).
                float sa = max(fwidth(sliver) * .9, .002);
                float streak = Crisp(_SliverScale.z, sliver, sa) * Crisp(_Bands.x * .8, vb, aa) * (1 - Crisp(_Bands.z, vb, aa));
                col = lerp(col, lerp(_Light.rgb, _Hot.rgb, .5), streak * _Streaks);
                float sheenS = frac((u + (v - .5) * .10) * _SheenRepeat - global * _SheenRate);
                float sw = max(fwidth(sheenS) * 1.5, .004);
                float sheen = (1 - Crisp(_SheenWidth, min(sheenS, 1 - sheenS), sw)) * Crisp(_Bands.y - .1, vb, aa) * (1 - Crisp(1.0, vb, aa));
                col = lerp(col, _Light.rgb * 1.25, sheen * _Sheen);

                // Шипы: бока индиго, к сердцевине светлее, сердцевина — горячая; у верха тела — горячая полоса (середина вала).
                float above = Crisp(1.0, v / max(topBody, .05), aa);
                float ca = max(fwidth(coreP) * 1.2, .004);
                half3 spikeCol = lerp(_Mid.rgb * 1.15, _Light.rgb, Crisp(.30, coreP, ca));
                col = lerp(col, spikeCol, above);
                float hotFrom = _Bands.z - (coarse - .5) * .14 - young * _FrontHot;
                float hotBody = Crisp(hotFrom, vb, aa);
                float hotSpike = Crisp(_SpikeCore - young * _FrontHot, coreP, ca) * (1 - Crisp(.86, (v - topBody) / max(spike, .02), aa));
                float hot = lerp(hotBody, hotSpike, above);
                half3 hotCol = lerp(_HotShade.rgb, _Hot.rgb, step(.42, fine));
                col = lerp(col, hotCol, hot);

                col = lerp(_Outline.rgb, col, body);
                float alpha = shape * lerp(_Outline.a, 1, body) * fade * i.color.a;
                half3 glow = hotCol * (hot * (1 + young) + sheen * .4) * body * shape * fade * _Glow;
                half3 rgb = (col * alpha + glow) * i.color.rgb;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
