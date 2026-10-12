Shader "Razlom/Anchor Throw Form Glint"
{
    // УЗЛЫ СЕТИ НЕВОДА (07.10, Editor/PelagWreckComboVfxSetup.ThrowForm; квадраты строит Game.View/PelagAnchorThrowFormNet):
    // в покое — светящаяся точка (белое ядро, ореол морской зелени, как узлы кадра form-net-v1); во вспышке (вершинный r)
    // поверх проступает четырёхлучевая блёстка пака «cfxr magic star» (08.10 V7: блёстка в покое на каждом узле — конфетти).
    // Без чернил. Вершинный a — непрозрачность.
    Properties
    {
        _MainTex ("Glint (r: pack draws the shape in rgb, alpha is 1)", 2D) = "white" {}
        _Mid ("Form colour", Color) = (.012,.45,.21,1)
        _Hot ("Flare (white-green)", Color) = (.85,1.15,.95,1)
        _Gain ("Rest brightness", Range(0,3)) = 1.3
        _FlareGain ("Flare brightness", Range(0,3)) = 1.4
        _Soft ("Soft glow", Range(0,1)) = .35
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+39" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Anchor Throw Form Glint"
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
            half4 _Mid, _Hot;
            float4 _MainTex_ST;
            float _Gain, _FlareGain, _Soft;
            CBUFFER_END
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float flare = saturate(i.color.r);
                float2 q = i.uv.xy;
                float r = length(q - .5) * 2;
                // Точка: белое ядро и ореол краски (в покое — только она).
                float glow = saturate(1 - r);
                glow *= glow;
                float dotCore = 1 - smoothstep(.10, .30, r);
                // Блёстка пака: в покое — короткие лучи краски (узлы кадра искрятся), во вспышке — полная и белеет.
                float star = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, q).r * lerp(.38, 1, smoothstep(.05, .5, flare));
                float core = max(dotCore * (.75 + .25 * flare), smoothstep(.55, .95, star) * flare);
                half3 col = _Mid.rgb * (_Gain * (glow * .9 + star) + glow * _Soft * (.5 + flare));
                col = lerp(col, _Hot.rgb * _FlareGain, saturate(core));
                float alpha = saturate(glow * .55 + star * .9 + core) * i.color.a;
                return half4(col * i.color.a, alpha);
            }
            ENDHLSL
        }
    }
}
