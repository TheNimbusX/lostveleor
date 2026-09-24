// Дымка по краю лагеря (владелец 24 сентября: «чтобы камера и миникарта никогда не показывали,
// где кончается карта»). Кольцо полупрозрачных слоёв над стеной леса: прозрачность берётся из
// цвета вершин (у внутренней кромки 0, к краю мира — плотнее), мягкое пятнистое зерно по миру
// не даёт слою выглядеть ровной плёнкой. Без освещения и без записи глубины.
Shader "Razlom/Camp Edge Haze"
{
    Properties
    {
        _Color ("Цвет дымки", Color) = (0.12,0.17,0.15,1)
        _Density ("Плотность", Range(0,1.5)) = 0.8
        _NoiseScale ("Размер пятен, м", Range(2,60)) = 18
        _NoiseStrength ("Пятнистость", Range(0,1)) = 0.35
        _Drift ("Снос пятен, м/с", Range(0,1)) = 0.08
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "EdgeHaze"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Density;
                float _NoiseScale;
                half _NoiseStrength;
                float _Drift;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half4 color : COLOR; half fog : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = input.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.positionWS.xz / _NoiseScale + _Time.y * _Drift / _NoiseScale;
                float n = Noise(p) * .65 + Noise(p * 2.3 + 7.1) * .35;
                half alpha = saturate(input.color.a * _Density * (1.0 - _NoiseStrength + _NoiseStrength * 2.0 * n));
                half3 colour = MixFog(_Color.rgb * input.color.rgb, input.fog);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
}
