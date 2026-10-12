// Крушение · ВОЛНОРЕЗ (07.10, Editor/PelagWreckComboVfxSetup.Breakwater): плиты стены — НАСТОЯЩИЕ 3D-камни (меши пака
// RPG Tiny Fantasy Forest, копии в единичный ящик: x, z −.5….5, y 0…1 — низ плиты на земле), освещённые солнцем арены
// и зелёным светом фронта (доп. свет URP), с гранями (плоская нормаль из производных) — объём, не наклейка.
//  • камень: два тона по свету (порог с узким переходом), верх светлее, подошва темнее, края граней чуть темнее —
//    рисованно, без чёрной обводки (владелец 07.10: «обводка херовая»);
//  • свет морской зелени (#1FB37E) — В ЩЕЛЯХ между плитами: грани плиты, что смотрят вдоль полосы (на соседей; спина
//    плиты, наклонённой вперёд, видна сверху как раз в щели), светятся снизу до доли высоты _BackRise с рваной жёсткой
//    кромкой по сетке трещин (Hovl Noise29); бока — ниже (_SideRise); редкие толстые трещины у подошвы и шов у земли;
//    _Glow — сила (хлопок выхода, оседание, вспышка распада); ядро — бледная мята только в шве и у самой земли;
//  • распад (_Crumble 0…1): плита рвётся крупными кусками (ячейки _BreakScale), кромка разрыва светится;
//  • частицы-обломки (_VertexColor 1): цвет частицы множит камень, свечения и масок высоты нет (вершины уже в мире).
// Непрозрачный: тени отбрасывает и принимает (камни, мобы на гребне).
Shader "Razlom/Wreck Breakwater Slab"
{
    Properties
    {
        _CrackTex ("Crack cells (r: cell edges)", 2D) = "black" {}
        _CrackScale ("Crack tiling (object units)", Float) = 1.6
        _Stone ("Lit stone", Color) = (0.66,0.64,0.60,1)
        _StoneShade ("Shade stone", Color) = (0.34,0.33,0.34,1)
        _StoneTop ("Top face light", Color) = (0.80,0.78,0.72,1)
        _FootColor ("Foot (near ground) darkening", Color) = (0.30,0.27,0.25,1)
        _FootHeight ("Foot darkening height (0..1)", Range(0,1)) = 0.28
        _Tint ("Piece tint", Color) = (1,1,1,1)
        _LightThreshold ("Light threshold", Range(0,1)) = 0.48
        _LightFeather ("Light feather", Range(0.001,0.3)) = 0.05
        _AmbientWeight ("Ambient weight", Range(0,1.5)) = 0.45
        _EdgeDark ("Grazing edge darkening", Range(0,1)) = 0.22
        _Facet ("Flat facet normals", Range(0,1)) = 0.85
        [HDR] _GlowColor ("Glow (sea green)", Color) = (0.06,1.20,0.62,1)
        [HDR] _GlowCore ("Glow core (pale mint)", Color) = (0.75,1.60,1.20,1)
        _Glow ("Glow strength", Float) = 1
        _GlowRise ("Crack glow reach (share of height)", Range(0,1)) = 0.35
        _CrackWidth ("Crack line threshold", Range(0,1)) = 0.86
        _BackRise ("Seam glow on lane-facing faces: reach", Range(0,1)) = 0.5
        _SideRise ("Seam glow on side faces: reach", Range(0,1)) = 0.22
        _BackFacing ("Lane-facing threshold (|n.z|)", Range(0,1)) = 0.45
        _Rag ("Ragged glow edge (share of height)", Range(0,0.5)) = 0.14
        _BreakScale ("Crumble piece tiling", Float) = 0.55
        _RimFrom ("Edge glow: grazing threshold (1 - n.v)", Range(0,1)) = 0.62
        _RimRise ("Edge glow reach (share of height)", Range(0,1)) = 0.8
        _FootSeam ("Foot seam height (0..1)", Range(0,0.3)) = 0.07
        _Crumble ("Crumble 0..1", Range(0,1)) = 0
        _VertexColor ("Particle mode (vertex colour, no masks)", Float) = 0
        _PointLightGain ("Extra light gain (front light)", Float) = 1
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_CrackTex);
    SAMPLER(sampler_CrackTex);

    CBUFFER_START(UnityPerMaterial)
        float4 _CrackTex_ST;
        float _CrackScale;
        half4 _Stone, _StoneShade, _StoneTop, _FootColor, _Tint, _GlowColor, _GlowCore;
        half _FootHeight, _LightThreshold, _LightFeather, _AmbientWeight, _EdgeDark, _Facet;
        float _Glow, _GlowRise, _CrackWidth, _FootSeam, _Crumble, _VertexColor, _PointLightGain;
        float _BackRise, _SideRise, _BackFacing, _Rag, _BreakScale, _RimFrom, _RimRise;
    CBUFFER_END

    // Сетка трещин в местных координатах плиты: проекция по главной оси нормали (без шва на гранях — ячейки крупные).
    float CrackCells(float3 positionOS, float3 normalOS, float scale)
    {
        float3 a = abs(normalOS);
        float2 uv = a.y > a.x && a.y > a.z ? positionOS.xz : a.x > a.z ? positionOS.zy : positionOS.xy;
        return SAMPLE_TEXTURE2D(_CrackTex, sampler_CrackTex, uv * scale + float2(.37, .11)).r;
    }

    // Распад: края ячеек уходят первыми, ячейки сжимаются к центрам; 0 — целая плита.
    float CrumbleCut(float cells)
    {
        return (1.0 - cells) - _Crumble * 1.08 + 0.02;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardBreakwater"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float3 normalOS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half particle = step(0.5h, _VertexColor);
                float cells = CrackCells(input.positionOS, input.normalOS, _CrackScale);
                // Распад рвёт плиту крупными кусками (у обломков-частиц масок нет).
                float cut = CrumbleCut(CrackCells(input.positionOS, input.normalOS, _BreakScale));
                if (particle < 0.5h) clip(cut);

                float3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 smoothN = normalize(input.normalWS);
                float3 flatN = normalize(cross(ddx(input.positionWS), ddy(input.positionWS)));
                flatN = dot(flatN, viewWS) < 0 ? -flatN : flatN;
                float3 normalWS = normalize(lerp(smoothN, flatN, _Facet));

                float h = particle > 0.5h ? 0.5 : saturate(input.positionOS.y);
                half3 tint = _Tint.rgb * lerp(half3(1, 1, 1), input.color.rgb, particle);

                // Свет: два тона по порогу, тень от солнца, верх светлее, подошва темнее.
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndl = dot(normalWS, mainLight.direction) * 0.5h + 0.5h;
                half lit = smoothstep(_LightThreshold - _LightFeather, _LightThreshold + _LightFeather, ndl) * mainLight.shadowAttenuation;
                half top = smoothstep(0.42h, 0.78h, normalWS.y);
                half3 albedo = lerp(_Stone.rgb, _StoneTop.rgb, top * lit) * tint;
                half3 shade = _StoneShade.rgb * tint;
                half3 color = lerp(shade, albedo * mainLight.color.rgb, lit) + albedo * SampleSH(normalWS) * _AmbientWeight;
                half foot = (1.0h - smoothstep(0.0h, _FootHeight, h)) * (1.0h - particle);
                color = lerp(color, color * _FootColor.rgb / max(0.05h, _Stone.rgb), foot * 0.85h);
                half rim = 1.0h - saturate(dot(normalWS, viewWS));
                color *= 1.0h - _EdgeDark * smoothstep(0.55h, 0.92h, rim);

                // Зелёный свет фронта (точечный свет вида) ложится на грани — свет между плитами.
                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint li = 0; li < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); li++)
                {
                    Light dl = GetAdditionalLight(li, input.positionWS, half4(1, 1, 1, 1));
                    color += albedo * dl.color * saturate(dot(normalWS, dl.direction)) * dl.distanceAttenuation * dl.shadowAttenuation;
                }
                #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                    half wrap = saturate(dot(normalWS, light.direction) * 0.6h + 0.4h);
                    // Раунд 5: вплотную к свету затухание 1/d² давало круглое бело-голубое пятно на грани — не больше 1.
                    color += albedo * light.color * wrap * min(light.distanceAttenuation, 1.0) * _PointLightGain;
                LIGHT_LOOP_END
                #endif

                // Свет в щелях: грани, что смотрят вдоль полосы (на соседние плиты), светятся снизу до _BackRise, бока — до
                // _SideRise; верхняя кромка света рваная и жёсткая (сетка трещин сдвигает порог), не мягкий градиент.
                // Плюс редкие толстые трещины у подошвы и шов у земли; кромка распада.
                float3 nOS = normalize(input.normalOS);
                half lane = smoothstep(_BackFacing - 0.08h, _BackFacing + 0.08h, abs(nOS.z));
                half sideFace = smoothstep(0.45h, 0.6h, abs(nOS.x)) * (1.0h - lane);
                float rag = (cells - 0.5) * _Rag;
                float aa = max(fwidth(h) * 1.2, 1e-3);
                half backBand = 1.0h - smoothstep(_BackRise + rag - aa, _BackRise + rag + aa, h);
                half sideBand = 1.0h - smoothstep(_SideRise + rag - aa, _SideRise + rag + aa, h);
                float ca = max(fwidth(cells) * 0.9, 1e-3);
                half crack = smoothstep(_CrackWidth - ca, _CrackWidth + ca, cells) * (1.0h - smoothstep(_GlowRise * 0.6h, _GlowRise, h));
                half seam = 1.0h - smoothstep(_FootSeam - aa, _FootSeam + aa, h);
                half edge = (1.0h - smoothstep(0.0h, 0.06h, cut)) * step(0.001h, _Crumble);
                // Кромка силуэта (грани вскользь к взгляду — узкие бока плит в щелях): свет из-за плиты, почти до верха,
                // жёсткий порог; так щель между плитами читается светом, а не краской на подошве.
                half grazing = 1.0h - saturate(dot(normalWS, viewWS));
                half rimBand = 1.0h - smoothstep(_RimRise + rag - aa, _RimRise + rag + aa, h);
                half rimGlow = smoothstep(_RimFrom - 0.02h, _RimFrom + 0.02h, grazing) * rimBand;
                half glowMask = saturate(lane * backBand + sideFace * sideBand + crack + seam + edge + rimGlow) * (1.0h - particle);
                // Ядро: мята у земли (нижняя треть света) и в кромке распада; выше — чистая морская зелень.
                half core = saturate((1.0h - smoothstep(0.0h, max(0.02h, _BackRise * 0.35h), h)) * lane + seam + edge);
                half3 glow = lerp(_GlowColor.rgb, _GlowCore.rgb, core);
                half power = max(0.0h, _Glow);
                color = lerp(color, glow * min(power, 1.0h), saturate(glowMask * saturate(power * 1.5h)));
                color += glow * glowMask * max(0.0h, power - 1.0h) * 0.45h;

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; float3 normalOS : TEXCOORD1; };

            float3 _LightDirection;
            float3 _LightPosition;

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(output.positionCS);
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                if (_VertexColor < 0.5) clip(CrumbleCut(CrackCells(input.positionOS, input.normalOS, _BreakScale)));
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; float3 normalOS : TEXCOORD1; };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                if (_VertexColor < 0.5) clip(CrumbleCut(CrackCells(input.positionOS, input.normalOS, _BreakScale)));
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
