// Пена у камней и опор моста (владелец 24 сентября: «река чистая, но пустая»). Плоский квад на
// воде: кольцо вокруг препятствия, вытянутое по течению хвостом, рваное шумом, который сносит
// течением. Цвет — мягкий зеленовато-белый, как светлая кромка воды у берега.
Shader "Razlom/Camp Water Foam"
{
    Properties
    {
        _Color ("Цвет пены", Color) = (0.86,0.95,0.9,1)
        _Opacity ("Плотность", Range(0,1)) = 0.8
        _Flow ("Течение (м/с вдоль X квада)", Float) = 0.35
        _NoiseScale ("Размер клочков", Range(1,20)) = 6
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "WaterFoam"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Opacity;
                float _Flow;
                float _NoiseScale;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
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
                // Квад: препятствие в точке (0.3, 0.5), хвост уходит к u=1 по течению.
                float2 p = input.uv - float2(0.3, 0.5);
                float ring = abs(length(float2(p.x * (p.x > 0 ? 0.55 : 1.0), p.y)) - 0.13);
                float body = 1.0 - smoothstep(0.02, 0.12, ring);
                float tail = saturate(1.0 - abs(p.y) / (0.08 + p.x * 0.35)) * smoothstep(0.0, 0.1, p.x) * (1.0 - smoothstep(0.35, 0.7, p.x));
                float2 q = input.uv * _NoiseScale - float2(_Time.y * _Flow * _NoiseScale * 0.35, 0);
                float n = Noise(q) * 0.6 + Noise(q * 2.7 + 3.1) * 0.4;
                float shape = max(body, tail * 0.8) * smoothstep(0.35, 0.75, n + body * 0.25);
                float edge = 1.0 - smoothstep(0.38, 0.5, max(abs(input.uv.x - 0.5), abs(input.uv.y - 0.5)));
                return half4(_Color.rgb, saturate(shape * edge * _Opacity));
            }
            ENDHLSL
        }
    }
}
