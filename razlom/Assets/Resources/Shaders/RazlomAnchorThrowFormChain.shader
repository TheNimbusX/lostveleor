Shader "Razlom/Anchor Throw Form Chain"
{
    // ЦЕПЬ ФОРМ БРОСКА ЯКОРЯ (07.10, Editor/PelagWreckComboVfxSetup.ThrowForm; ленты строят Game.View/PelagAnchorThrowFormNet (сеть) и PelagAnchorThrowLinkStrip (звенья призрачной цепи)
    // кадр за кадром). Целевые кадры — form-net-v1 (сеть из цепей, морская зелень), form-fan-ghost-v1 (три цепи индиго,
    // призрачные звенья), form-harpoon-v2 (маджента). Одна лента — СВЕТ вокруг цепи и, если надо, сами ЗВЕНЬЯ:
    //  • поперёк (v −1…1): мягкий ореол цвета формы, насыщенная полоса с рваным мерцающим краем (шум пака течёт к руке),
    //    горячая почти белая сердцевина по оси (ширина — жар); чернил нет, край светится;
    //  • звенья (TEXCOORD1.y > 0): процедурная цепь — плашмя лежащее кольцо-овал и звено ребром по очереди, размер в
    //    метрах (TEXCOORD1.x — метры поперёк), свои цвета (_LinkBody/_LinkRim): железо сети, стекло призрачной цепи;
    //    у настоящей цепи рига звеньев в ленте нет — свет лежит ПОД звеньями (_CameraPush < 0);
    //  • u = метры от кольца головы к руке (рисунок едет с головой), TEXCOORD0.z — секунды распада, .w — часы с выпуска;
    //  • вершинный цвет: r — жар, g — шевроны рывка (к руке), b — импульс натяга, a — непрозрачность.
    Properties
    {
        _Deep ("Deep (halo edge)", Color) = (.01,.10,.06,1)
        _Mid ("Form colour", Color) = (.012,.45,.21,1)
        _Light ("Light", Color) = (.16,.86,.53,1)
        _Hot ("Hot core (near-white)", Color) = (.80,1.10,.92,1)
        _FacetTex ("Noise (r)", 2D) = "gray" {}
        _CoreW ("Core half (heat 1)", Range(0,1)) = .20
        _CoreW0 ("Core half (heat 0)", Range(0,1)) = .07
        _BodyW ("Band half", Range(0,1)) = .46
        _BodyAlpha ("Band opacity", Range(0,1)) = .92
        _HaloAlpha ("Halo opacity", Range(0,1)) = .28
        _HaloGain ("Halo brightness", Range(0,3)) = 1.0
        _Rag ("Band edge rag", Range(0,.6)) = .30
        _FlowSpeed ("Noise flow to the hand (m/s)", Float) = 2.5
        _Sparkle ("Sparkles in the halo", Range(0,1)) = .6
        _Chevrons ("Chevrons per metre", Float) = 1.8
        _ChevronRate ("Chevrons per second (to the hand)", Float) = 9
        _PulseGain ("Pulse brightness", Range(0,2)) = .9
        _GlowAlpha ("Glow opacity (0 = links only)", Range(0,1)) = 1
        _Links ("Draw links", Range(0,1)) = 0
        _LinkPitch ("Link pitch (m)", Float) = .105
        _LinkHalf ("Link half width (m)", Float) = .042
        _LinkThick ("Link wire thickness (m)", Float) = .018
        _LinkBody ("Link body", Color) = (.07,.08,.10,1)
        _LinkRim ("Link rim (lit edge)", Color) = (.45,.55,.60,1)
        _LinkShadow ("Link outer contour", Color) = (.02,.03,.03,1)
        _LinkGlass ("Link alpha (1 iron, <1 glass)", Range(0,1)) = 1
        _ErodeFrom ("Erode from (s)", Float) = .03
        _ErodeTo ("Erode to (s)", Float) = .22
        _FadeFrom ("Fade from (s)", Float) = .16
        _FadeTo ("Fade to (s)", Float) = .30
        _Opacity ("Opacity", Range(0,1)) = 1
        _CameraPush ("Push toward camera (m)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+37" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Anchor Throw Form Chain"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float4 uv : TEXCOORD0; float4 uv1 : TEXCOORD1; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 uv : TEXCOORD0; float4 uv1 : TEXCOORD1; half4 color : COLOR; };

            CBUFFER_START(UnityPerMaterial)
            half4 _Deep, _Mid, _Light, _Hot, _LinkBody, _LinkRim, _LinkShadow;
            float4 _FacetTex_ST;
            float _CoreW, _CoreW0, _BodyW, _BodyAlpha, _HaloAlpha, _HaloGain, _Rag, _FlowSpeed, _Sparkle, _Chevrons, _ChevronRate;
            float _PulseGain, _GlowAlpha, _Links, _LinkPitch, _LinkHalf, _LinkThick, _LinkGlass;
            float _ErodeFrom, _ErodeTo, _FadeFrom, _FadeTo, _Opacity, _CameraPush;
            CBUFFER_END
            TEXTURE2D(_FacetTex); SAMPLER(sampler_FacetTex);

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                positionWS += GetWorldSpaceNormalizeViewDir(positionWS) * _CameraPush;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = v.uv;
                o.uv1 = v.uv1;
                o.color = v.color;
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            // Звено: плашмя — овальное кольцо, ребром — продолговатая капсула; расстояние в метрах (<0 — металл).
            float LinkDistance(float along, float across, float index)
            {
                float a = _LinkPitch * .64, b = _LinkHalf;
                float dx = along - (index + .5) * _LinkPitch;
                if (fmod(abs(index), 2) < .5)
                {
                    float e = (length(float2(dx / a, across / b)) - 1) * min(a, b) * 1.05;
                    return abs(e) - _LinkThick * .5;
                }
                float hl = a - _LinkThick * .6;
                float px = max(abs(dx) - hl, 0);
                return length(float2(px, across)) - _LinkThick * .62;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float u = i.uv.x, v = i.uv.y, av = abs(v);
                float age = max(0, i.uv.z), clock = i.uv.w;
                float heat = i.color.r, chev = i.color.g, pulse = i.color.b;
                float flow = clock * _FlowSpeed;

                float coarse = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, float2((u + flow) * .55, v * .30 + .17)).r;
                float fine = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, float2((u + flow * 1.6) * 1.9 + .41, v * .8 + .63)).r;

                // Распад: дыры гранями пака открываются от краёв к оси, потом всё гаснет.
                float erodeP = 1.05 * smoothstep(_ErodeFrom, max(_ErodeFrom + .01, _ErodeTo), age);
                float field = coarse * .85 + fine * .15 + .06 - av * .25;
                float keep = smoothstep(erodeP - .05, erodeP + .05, field);
                float fade = _Opacity * i.color.a * (1 - smoothstep(_FadeFrom, max(_FadeFrom + .01, _FadeTo), age));

                // Поперечный профиль: ореол → полоса (рваный край течёт к руке) → горячая ось.
                float aa = max(fwidth(av) * 1.2, .004);
                float bodyW = _BodyW * (1 - _Rag * .5 + _Rag * coarse) * lerp(.80, 1.0, heat) + pulse * .14;
                float coreW = lerp(_CoreW0, _CoreW, saturate(heat)) * lerp(.85, 1.1, fine) + pulse * .10;
                float band = 1 - smoothstep(bodyW - aa, bodyW + aa, av);
                float core = 1 - smoothstep(coreW - aa, coreW + aa, av);
                float halo = saturate(1 - av);
                halo = halo * halo * (.65 + .7 * coarse);

                // Искры в ореоле: редкие точки, бегут к руке.
                float2 cell = float2(floor((u + flow * 1.3) * 9), floor(v * 3));
                float2 fr = float2(frac((u + flow * 1.3) * 9), frac(v * 3));
                float h = Hash(cell);
                float spark = step(.82, h) * saturate(1 - length((fr - float2(.5, .5)) * float2(2.2, 3.2)) * 1.6) * (1 - band) * _Sparkle;

                // Шевроны рывка «>» к руке в полосе.
                float cs = frac(u * _Chevrons - av * .5 - clock * _ChevronRate);
                float chevron = (1 - smoothstep(.10, .16, min(cs, 1 - cs))) * band * (1 - core) * chev;

                half3 bandCol = lerp(_Mid.rgb, _Light.rgb, saturate(1 - (av - coreW) / max(.01, bodyW - coreW)) * .75);
                bandCol = lerp(bandCol, _Light.rgb, chevron);
                half3 haloCol = lerp(_Deep.rgb, _Mid.rgb, halo) * _HaloGain;

                float haloA = halo * _HaloAlpha;
                half3 col = haloCol * haloA + _Light.rgb * spark;
                float alpha = haloA;
                col = lerp(col, bandCol, band * _BodyAlpha);
                alpha = lerp(alpha, 1, band * _BodyAlpha);
                col = lerp(col, _Hot.rgb, core);
                alpha = max(alpha, core);
                col *= 1 + pulse * _PulseGain;
                // Только звенья (призрачная цепь Веера: свет вокруг рисует струна базы).
                col *= _GlowAlpha;
                alpha *= _GlowAlpha;

                // Звенья поверх света.
                float linkVis = _Links * saturate(i.uv1.y);
                if (linkVis > .001)
                {
                    float across = i.uv1.x;
                    float s = u / _LinkPitch;
                    float k = floor(s);
                    float d = min(LinkDistance(u, across, k), min(LinkDistance(u, across, k - 1), LinkDistance(u, across, k + 1)));
                    float la = max(fwidth(d) * 1.1, 1e-4);
                    float metal = 1 - smoothstep(-la, la, d);
                    float rim = smoothstep(-_LinkThick * .55, -_LinkThick * .1, d);
                    half3 linkCol = lerp(_LinkBody.rgb, _LinkRim.rgb, rim * .8);
                    float contour = (1 - smoothstep(la, la * 3.5, d)) * (1 - metal);
                    float m = metal * linkVis;
                    col = lerp(col, _LinkShadow.rgb, contour * linkVis * .6);
                    alpha = lerp(alpha, 1, contour * linkVis * .6);
                    col = lerp(col, linkCol, m * _LinkGlass) + linkCol * m * (1 - _LinkGlass) * .6;
                    alpha = lerp(alpha, 1, m * _LinkGlass);
                }

                float k2 = keep * fade;
                return half4(col * k2, alpha * k2);
            }
            ENDHLSL
        }
    }
}
