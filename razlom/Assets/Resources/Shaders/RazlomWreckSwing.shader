// Крушение «холодное железо» — махи, вид V2 (06.10, swing1.png / swing2.png; вид — PelagVfxController.WreckSwing*.cs,
// числа — PelagWreckSwingRules / PelagWreckSwingImpact, цвета — PelagWreckSwingLook, сборка — Editor/PelagWreckSwingVfxSetup V2).
// Premultiplied (Blend One OneMinusSrcAlpha): тёмный контур — альфой, холодный свет — HDR под bloom. Без молний, пены, красного
// и золота. Цвета формы — блоком свойств: _Tint (ядро), _TintLight (светлое ядро к кромке).
// _Mode 0 — СЕРП (меш PelagWreckSwingRibbon): маска пака CFXR «sword trail mask lines» (рецепт CFXR «sword trail slash»:
//   сплошное тело, хвост и края рвутся штрихами вдоль хода) по u/v, распад шумом CFXR «perlin mid» (хвост первым);
//   поперёк: ядро формы → светлое ядро → белая наружная кромка; толстый тёмный контур по краям и рваной кромке.
//   uv0 = (u 0 голова…1 хвост, v 0 внутренний…1 наружный край, метры кромки от головы, ширина полосы, м),
//   uv1 = (непрозрачность, распад 0…1, зерно, яркость маха).
// _Mode 1 — ЗВЕНО-ПРИЗРАК (меш звена «железа» базы WreckIronLink): светлое ядро формы к белому, у силуэта прута —
//   чернильный контур (N·V), прозрачность _Fade (блок свойств).
// _Mode 2 — РОСЧЕРК (растянутые частицы, лист CFXR «stretch trait», одноканальный R): цвет и альфа — частицы
//   (контур «Ink» — тёмной частицей под телом).
// _Mode 3 — СКОЛ (частицы, лист обломков CFXR «debris unlit 3x3» 3×3): тело (R≈1) — тёмное железо _IronColor,
//   кромка пака (R≈0,5) — цвет частицы (светлое ядро формы), альфа — A листа × частицы.
Shader "Razlom/Wreck Swing"
{
    Properties
    {
        _MainTex ("Pack Mask / Sheet", 2D) = "white" {}
        _NoiseTex ("Dissolve Noise (R)", 2D) = "gray" {}
        [HDR] _Tint ("Form Core", Color) = (0.31,0.66,1,1)
        [HDR] _TintLight ("Form Light Core", Color) = (0.61,0.85,1,1)
        _Mode ("Mode (0 crescent, 1 link, 2 streak, 3 chip)", Float) = 0
        _Glow ("Glow (HDR multiplier)", Float) = 1.5
        _EdgeFrom ("White Edge From (v)", Range(0,1)) = 0.8
        _Opacity ("Opacity", Range(0,1)) = 0.95
        _OutlineColor ("Outline", Color) = (0.055,0.04,0.08,1)
        _OutlineMeters ("Outline Width (m)", Float) = 0.055
        _Ink ("Ragged Outline (mask units)", Range(0,0.5)) = 0.16
        _Cut ("Base Cut", Range(0,1)) = 0.1
        _MaskFrom ("Mask x at head", Range(0,1)) = 0.42
        _MaskTo ("Mask x at tail", Range(0,1)) = 0.99
        _NoiseMeters ("Noise Tile Along (m)", Float) = 1.4
        _Fade ("Link Fade", Range(0,1)) = 1
        _RimFrom ("Link Ink From (1 - N.V)", Range(0,1)) = 0.55
        _IronColor ("Chip Iron", Color) = (0.15,0.165,0.2,1)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "WreckSwing"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_linear_repeat);
            SAMPLER(sampler_linear_clamp);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                half4 _TintLight;
                float _Mode;
                half _Glow;
                half _EdgeFrom;
                half _Opacity;
                half4 _OutlineColor;
                float _OutlineMeters;
                half _Ink;
                half _Cut;
                half _MaskFrom;
                half _MaskTo;
                float _NoiseMeters;
                half _Fade;
                half _RimFrom;
                half4 _IronColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
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
                float3 positionWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv0 = input.uv0;
                output.uv1 = input.uv1;
                output.color = input.color;
                return output;
            }

            half4 Crescent(Varyings input)
            {
                float u = input.uv0.x, v = input.uv0.y, meters = input.uv0.z, width = max(1e-3, input.uv0.w);
                half alpha = (half)input.uv1.x, erode = (half)input.uv1.y, glow = (half)input.uv1.w;
                float seed = input.uv1.z;
                // Маска пака: x — вдоль (у головы сплошная, к хвосту рвётся штрихами), y — поперёк (рваные края).
                half mask = SAMPLE_TEXTURE2D(_MainTex, sampler_linear_clamp, float2(lerp(_MaskFrom, _MaskTo, u), lerp(0.04, 0.96, v))).r;
                half noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_linear_repeat, float2(meters / _NoiseMeters + seed, v * 0.45 + seed * 0.37)).r;
                // У самой головы поле сходит в ноль — контур замыкается и спереди (голову закрывает якорь).
                half field = mask * (0.8h + 0.4h * noise) * smoothstep(0.0h, 0.025h, (half)u);
                // Распад хвостом вперёд; медленный ход и хвост прозрачны порогом, а не полупрозрачностью.
                half cut = _Cut + erode * (1.15h + 0.5h * (half)u) + (1.0h - alpha) * 0.95h;
                half body = smoothstep(cut, cut + 0.04h, field);
                half ink = smoothstep(cut - _Ink, cut - _Ink * 0.5h, field);
                // Контур по краям полосы в метрах (на тонком хвосте — тоньше) и по рваной кромке маски.
                float edgeMeters = min(v, 1.0 - v) * width;
                float outlineMeters = min(_OutlineMeters * (0.85 + 0.3 * glow), width * 0.28);
                half edge = 1.0h - smoothstep(outlineMeters * 0.7, outlineMeters, edgeMeters);
                half outline = saturate(max(ink - body, edge * ink));

                half3 core = lerp(_Tint.rgb, _TintLight.rgb, smoothstep(0.3h, 0.85h, (half)v));
                half white = smoothstep(_EdgeFrom, _EdgeFrom + 0.12h, (half)v);
                half3 color = lerp(core, half3(1, 1, 1), white * 0.9h) * (_Glow * glow) * (1.0h + 0.25h * (1.0h - (half)u));
                half a = body * _Opacity;
                half o = outline * _OutlineColor.a;
                half3 rgb = color * a * (1.0h - o) + _OutlineColor.rgb * o;
                return half4(rgb, saturate(max(a, o)));
            }

            half4 Link(Varyings input)
            {
                float3 n = normalize(input.normalWS);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half rim = 1.0h - (half)abs(dot(n, view));
                half inkK = smoothstep(_RimFrom, _RimFrom + 0.2h, rim);
                half3 core = lerp(_TintLight.rgb, half3(1, 1, 1), 0.45h) * _Glow;
                half a = saturate(_Fade) * _Opacity;
                half3 rgb = lerp(core, _OutlineColor.rgb, inkK) * a;
                return half4(rgb, a);
            }

            half4 Streak(Varyings input)
            {
                half mask = SAMPLE_TEXTURE2D(_MainTex, sampler_linear_clamp, input.uv0.xy).r;
                half a = mask * input.color.a;
                return half4(input.color.rgb * _Glow * a, a);
            }

            half4 Chip(Varyings input)
            {
                half4 sheet = SAMPLE_TEXTURE2D(_MainTex, sampler_linear_clamp, input.uv0.xy);
                half rim = 1.0h - smoothstep(0.62h, 0.9h, sheet.r);
                half3 rgb = lerp(_IronColor.rgb, input.color.rgb * _Glow, rim);
                half a = sheet.a * input.color.a;
                return half4(rgb * a, a);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // Без MixFog: у premultiplied туман лёг бы дымкой и там, где альфа ноль.
                if (_Mode < 0.5) return Crescent(input);
                if (_Mode < 1.5) return Link(input);
                if (_Mode < 2.5) return Streak(input);
                return Chip(input);
            }
            ENDHLSL
        }
    }
}
