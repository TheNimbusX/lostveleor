Shader "Razlom/Anchor Throw Furrow"
{
    // БОРОЗДА ВОЛОКА БРОСКА ЯКОРЯ (07–08.10, Editor/PelagWreckComboVfxSetup.ThrowIron; лента по земле строится кадр за кадром в
    // Game.View/PelagAnchorThrowIronStrip по настоящему пути тянутого тела). Целевой кадр — base-yank-v1: тёмная
    // вспаханная канава шириной в тело, светлые вывернутые бортики с комками, рваный край. ЕДИНСТВЕННЫЙ плоский слой
    // рывка: объём дают 3D-комья, комья-спрайты и пыль у переднего края (контроллер).
    //  • u — метры вдоль пути / 2, v — поперёк (−1…1, |v| = 0 — дно);
    //  • TEXCOORD0.z — возраст точки (с тех пор, как через неё прошло тело): свежая у тела темнее и влажнее, старая
    //    рассыпается гранями пака с краёв к середине и гаснет; TEXCOORD0.w — секунды с рывка;
    //  • вершинный цвет a — непрозрачность (начало и передний край скруглены шириной в контроллере).
    // Без свечения и без чернил: земля, тонкий тёмно-бурый край. Смешивание премультиплированное.
    Properties
    {
        _Trench ("Trench bottom", Color) = (.16,.09,.05,1)
        _Wall ("Trench wall", Color) = (.30,.18,.09,1)
        _WallShade ("Trench wall in shadow", Color) = (.21,.12,.06,1)
        _Lip ("Lip (turned earth)", Color) = (.62,.43,.26,1)
        _LipShade ("Lip shade / clods", Color) = (.40,.26,.14,1)
        _Fresh ("Fresh (wet) tint", Color) = (.70,.62,.58,1)
        _Rim ("Rim", Color) = (.12,.07,.04,.55)
        _FacetTex ("Facet noise (r)", 2D) = "gray" {}
        _FacetScale ("Noise scale (fine u, fine v, coarse u, coarse v)", Vector) = (2.6,1.6,1.1,.7)
        _Bands ("Bands |v| (bottom end, wall end, lip rag, clods threshold)", Vector) = (.30,.60,.10,.70)
        _EdgeRag ("Edge rag", Range(0,.6)) = .30
        _RimPx ("Rim width, px", Range(0,4)) = 1.4
        _FreshSeconds ("Fresh for (s)", Float) = .35
        _ErodeGain ("Erosion edge in width units", Float) = 1.4
        _ErodeFrom ("Erode from (local s)", Float) = .9
        _ErodeTo ("Erode to (local s)", Float) = 1.6
        _FadeFrom ("Fade from (local s)", Float) = 1.3
        _FadeTo ("Fade to (local s)", Float) = 1.8
        _Opacity ("Opacity", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Anchor Throw Furrow"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float4 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 uv : TEXCOORD0; half4 color : COLOR; };

            CBUFFER_START(UnityPerMaterial)
            half4 _Trench, _Wall, _WallShade, _Lip, _LipShade, _Fresh, _Rim;
            float4 _FacetTex_ST, _FacetScale, _Bands;
            float _EdgeRag, _RimPx, _FreshSeconds, _ErodeGain, _ErodeFrom, _ErodeTo, _FadeFrom, _FadeTo, _Opacity;
            CBUFFER_END
            TEXTURE2D(_FacetTex); SAMPLER(sampler_FacetTex);

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float Crisp(float edge, float x, float aa) { return smoothstep(edge - aa, edge + aa, x); }

            half4 Frag(Varyings i) : SV_Target
            {
                float u = i.uv.x, v = i.uv.y, av = abs(v);
                float age = max(0, i.uv.z);
                float erodeP = 1.05 * smoothstep(_ErodeFrom, max(_ErodeFrom + .01, _ErodeTo), age);
                float fade = _Opacity * (1 - smoothstep(_FadeFrom, max(_FadeFrom + .01, _FadeTo), age));
                float fine = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, float2(u * _FacetScale.x, v * _FacetScale.y)).r;
                float coarse = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, float2(u * _FacetScale.z + .37, v * _FacetScale.w + .21)).r;
                float clump = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, float2(u * 4.3 + .71, v * 2.9 + .17)).r;

                // Край — рваный комьями (крупные грани), распад — от краёв к дну по возрасту.
                float dOuter = (1 - _EdgeRag * (1 - coarse)) - av;
                float dErode = (coarse * (1 - av * .5) - erodeP) * _ErodeGain;
                float d = min(dOuter, dErode);
                float px = d / max(fwidth(d), 1e-5);
                float shape = saturate(px + .5);
                float body = saturate(px - _RimPx + .5);

                // Дно → стенки (одна в тени) → вывернутые бортики с комками; границы ступенятся по граням.
                float vb = saturate(av + (fine - .5) * .10 + (coarse - .5) * _Bands.z);
                float aa = max(fwidth(vb) * 1.2, .003);
                half3 wall = v > 0 ? _Wall.rgb : _WallShade.rgb;
                half3 col = _Trench.rgb;
                col = lerp(col, wall, Crisp(_Bands.x, vb, aa));
                half3 lip = lerp(_LipShade.rgb, _Lip.rgb, step(.42, fine));
                float clod = Crisp(_Bands.w, clump, max(fwidth(clump), .002));
                lip = lerp(lip, _LipShade.rgb * .8, clod);
                col = lerp(col, lip, Crisp(_Bands.y, vb, aa));
                // Борозда на дне: тёмные продольные штрихи (волок), бугорки светлее.
                col *= lerp(.82, 1.08, fine);
                // Свежая у тела — темнее и влажнее.
                float fresh = 1 - smoothstep(0, max(.01, _FreshSeconds), age);
                col *= lerp(half3(1, 1, 1), _Fresh.rgb, fresh);

                col = lerp(_Rim.rgb, col, body);
                float alpha = shape * lerp(_Rim.a, 1, body) * fade * i.color.a;
                return half4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
