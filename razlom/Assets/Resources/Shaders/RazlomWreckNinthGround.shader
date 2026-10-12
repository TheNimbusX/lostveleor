Shader "Razlom/Wreck Ninth Ground"
{
    // ДЕВЯТЫЙ ВАЛ — вал по земле (07.10, Editor/PelagWreckComboVfxSetup.Ninth; полосу строит Game.View/PelagWreckNinthCrest
    // .BuildGround кадр за кадром). Второй слой под стоячим веером: рисунок целевого кадра v4-form-ninthwave — шипастый вал
    // индиго, шире у точки удара и острый вперёд, свет внутри (бело-фиолетовая середина, светлые прожилки к шипам), край
    // шипов — индиго с тонким тёмно-индиговым контуром (не чёрным). Земля вокруг не заливается: всё — внутри силуэта.
    //  • u — метры вдоль полосы / 4, v — поперёк (−1 … +1, край вала Sim × разлёт шипов), z — возраст точки (с тех пор, как
    //    прошёл фронт Sim), w — возраст удара; распад гранями пака «noise ice» и порог — как у гребней.
    Properties
    {
        _Deep ("Deep indigo (edge)", Color) = (.05,.03,.38,1)
        _Mid ("Indigo", Color) = (.07,.05,.63,1)
        _Light ("Light violet", Color) = (.31,.26,1,1)
        _Hot ("Hot white-violet", Color) = (.95,.92,1.30,1)
        _Outline ("Contour (dark indigo)", Color) = (.012,.004,.058,.9)
        _FacetTex ("Facet noise (r)", 2D) = "gray" {}
        _Body ("Body half-width (v), rag", Vector) = (.40,.10,0,0)
        _Spikes ("Big spikes (per u, min, max, needle share)", Vector) = (8,.18,.56,.22)
        _SmallSpikes ("Small spikes (per u, min, max, width min)", Vector) = (20,.06,.20,.45)
        _SpikeLean ("Spike lean forward (u per v)", Float) = .10
        _SpikeSharp ("Spike side power", Float) = 1.6
        _Core ("Core: hot half-width, light half-width, spike core from", Vector) = (.10,.30,.70,0)
        _OutlinePx ("Contour width, px", Range(0,6)) = 1.5
        _ErodeGain ("Erosion edge in width units", Float) = 1.6
        _ErodeFrom ("Erode from (local s)", Float) = .16
        _ErodeTo ("Erode to (local s)", Float) = .46
        _ErodeBias ("Erosion field bias", Range(-.3,.3)) = .02
        _FadeFrom ("Fade from (local s)", Float) = .40
        _FadeTo ("Fade to (local s)", Float) = .52
        _Streaks ("Light streaks", Range(0,1)) = .6
        _Glow ("Hot glow", Range(0,1)) = .30
        _FlowSpeed ("Flow speed (u/s)", Float) = 1.2
        _CameraPush ("Push toward camera (m)", Float) = .02
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+38" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Wreck Ninth Ground"
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
            half4 _Deep, _Mid, _Light, _Hot, _Outline;
            float4 _FacetTex_ST, _Body, _Spikes, _SmallSpikes, _Core;
            float _SpikeLean, _SpikeSharp, _OutlinePx, _ErodeGain, _ErodeFrom, _ErodeTo, _ErodeBias, _FadeFrom, _FadeTo;
            float _Streaks, _Glow, _FlowSpeed, _CameraPush;
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
                float u = i.uv.x, v = i.uv.y, a = abs(v);
                float local = max(0, i.uv.z), global = max(0, i.uv.w);
                float erodeP = 1.05 * smoothstep(_ErodeFrom, max(_ErodeFrom + .01, _ErodeTo), local);
                float fade = 1 - smoothstep(_FadeFrom, max(_FadeFrom + .01, _FadeTo), local);
                float flow = global * _FlowSpeed;
                float young = 1 - smoothstep(0, .08, local);
                float fine = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, float2((u - flow) * 1.4, a * 2.0)).r;
                float coarse = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, float2((u - flow * .5) * .9 + .37, a * .8 + .21 + step(0, v) * .5)).r;
                float sliver = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, float2((u - flow * 1.6) * .5 + .61, a * 5.0 + .13)).r;

                // Силуэт: тело до рваной кромки, наружу — шипы двух рядов, у каждой стороны свои (сид по знаку v).
                float side = v >= 0 ? 0 : 37.0;
                float bodyEdge = _Body.x - _Body.y * (1 - coarse);
                float out0 = max(0, a - bodyEdge);
                float coreA, coreB;
                float sA = SpikeRow((u - _SpikeLean * out0) * _Spikes.x + side * .13, _Spikes.y, _Spikes.z, .28, _Spikes.w, side, coreA);
                float sB = SpikeRow((u - _SpikeLean * 1.4 * out0) * _SmallSpikes.x + .5 + side * .07, _SmallSpikes.y, _SmallSpikes.z,
                    _SmallSpikes.w, 0, 17.0 + side, coreB);
                float spike = max(sA, sB) * (1 + .3 * young);
                float coreP = sA >= sB ? coreA : coreB;
                float edge = min(.995, bodyEdge + spike);
                float dOuter = edge - a;
                float dErode = (coarse - _ErodeBias - erodeP) * _ErodeGain;
                float d = min(dOuter, dErode);
                float px = d / max(fwidth(d), 1e-5);
                float shape = saturate(px + .5);
                float body = saturate(px - _OutlinePx + .5);

                // Свет: середина бело-фиолетовая, вокруг светлый фиолетовый, тело индиго, к краю тела глубже; шипы — индиго
                // со светлой сердцевиной; светлые прожилки текут вперёд.
                float ab = a / max(bodyEdge, .05) + (fine - .5) * .08;
                float aa = max(fwidth(ab) * 1.2, .003);
                half3 col = lerp(_Mid.rgb, _Deep.rgb, Crisp(.82, ab, aa));
                col = lerp(_Light.rgb, col, Crisp(_Core.y / max(bodyEdge, .05), ab, aa));
                col *= lerp(.90, 1.06, fine);
                float sa = max(fwidth(sliver) * .9, .002);
                float streak = Crisp(.80, sliver, sa) * (1 - Crisp(.95, ab, aa));
                col = lerp(col, lerp(_Light.rgb, _Hot.rgb, .4), streak * _Streaks);
                float outside = Crisp(1.0, ab, aa);
                float ca = max(fwidth(coreP) * 1.2, .004);
                half3 spikeCol = lerp(_Mid.rgb, _Light.rgb, Crisp(.45, coreP, ca));
                col = lerp(col, spikeCol, outside);
                float hotBody = 1 - Crisp((_Core.x + young * .06) / max(bodyEdge, .05), ab, aa);
                float hotSpike = Crisp(_Core.z, coreP, ca) * (1 - Crisp(.70, out0 / max(spike, .02), aa));
                float hot = lerp(hotBody, hotSpike, outside);
                col = lerp(col, _Hot.rgb, hot);

                col = lerp(_Outline.rgb, col, body);
                float alpha = shape * lerp(_Outline.a, 1, body) * fade * i.color.a;
                half3 glow = _Hot.rgb * hot * (1 + young) * body * shape * fade * _Glow;
                return half4((col * alpha + glow) * i.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
