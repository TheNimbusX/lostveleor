Shader "Razlom/Wreck Ghost Anchor Glow"
{
    // ОРЕОЛ Призрачного якоря (07.10, вместо чёрного обвода «… Ink», к «Razlom/Wreck Ghost Anchor»): оболочка меша,
    // раздутая по СГЛАЖЕННЫМ нормалям (TEXCOORD1 копии меша — у Tripo нормали рвутся по швам) на постоянную ширину в
    // пикселях; задние грани; глазурь + свет (премультипл.: земля под ореолом холодеет в фиолетовый и светлеет, как у
    // целевого кадра; одним сложением на оранжевой земле выходил розовый) — ступень светлого фиолетового вокруг
    // силуэта, без мягкой дымки и без чернил. Две оболочки (внутренняя ярче и уже, внешняя шире и тусклее) делят трафарет: тело пишет
    // 200, внутренняя рисуется только где 0 и пишет _StencilRef, внешняя — только где 0 (Ref меньше) — ни одна не
    // ложится на тело и не складывается сама с собой. Срез проявления и распада — тот же, что у тела.
    Properties
    {
        _Glow ("Glow (premultiplied colour, HDR)", Color) = (.40,.32,1,1)
        _Alpha ("Glaze opacity", Range(0,1)) = .5
        _WidthPx ("Shell width, px at 1080p", Range(0,24)) = 6
        _StencilRef ("Stencil ref (inner 100, outer 50)", Float) = 100
        _FacetTex ("Facet noise (r)", 2D) = "gray" {}
        _NoiseScale ("Noise scale (facets xy, cut xy) per mesh unit", Vector) = (.65,.9,1.1,1.4)
        _Appear ("Appear 0..1 (controller)", Range(0,1)) = 1
        _Dissolve ("Dissolve 0..1, bottom first (controller)", Range(0,1)) = 0
        _Flash ("Impact flash 0..1 (controller)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+37" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Wreck Ghost Anchor Glow"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Front
            Stencil { Ref [_StencilRef] Comp Greater Pass Replace }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 smoothOS : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
            half4 _Glow;
            float4 _FacetTex_ST, _NoiseScale;
            float _WidthPx, _StencilRef, _Appear, _Dissolve, _Flash, _Alpha;
            CBUFFER_END
            TEXTURE2D(_FacetTex); SAMPLER(sampler_FacetTex);

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 n = dot(v.smoothOS, v.smoothOS) > .01 ? v.smoothOS : v.normalOS;
                float4 cs = TransformObjectToHClip(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(n);
                float2 dir = mul((float3x3)GetWorldToHClipMatrix(), nWS).xy;
                dir = dir / max(length(dir), 1e-5);
                // Ширина задана для 1080p; на другом экране — та же доля высоты кадра. Во вспышку ореол шире.
                float px = _WidthPx * (_ScreenParams.y / 1080.0) * (1 + _Flash * .6);
                cs.xy += dir * (px * 2.0 / _ScreenParams.xy) * cs.w;
                o.positionCS = cs;
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float coarse = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, i.positionOS.xy * _NoiseScale.zw + float2(.37, .21)).r;
                float h = saturate(i.positionOS.y);
                float dissolve = (h * .62 + coarse * .38) - (_Dissolve * 1.12 - .06);
                float appear = coarse - (1.06 - _Appear * 1.12);
                clip(min(dissolve, appear));
                return half4(_Glow.rgb * (1 + _Flash * .35), _Alpha);
            }
            ENDHLSL
        }
    }
}
