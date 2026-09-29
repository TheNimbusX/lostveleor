Shader "Razlom/Forest Acid Grass"
{
    // Пучки травы вокруг живой лужи V6: при шлепке примяты, потом вянут —
    // буреют от корня к кончику и ложатся наружу, от кислоты. Один меш на
    // лужу (ForestPuddleSurface): у вершины TEXCOORD1 = корень пучка и его
    // высота, TEXCOORD2 = направление «от лужи», начало увядания, случайное;
    // цвет вершины — разброс оттенка пучков (Color32). Всё движение — от
    // возраста Sim, общий шейдер травы лагеря не трогаем.
    Properties
    {
        _BaseMap ("Пучок травы", 2D) = "white" {}
        _Fresh ("Живая трава", Color) = (.80,.84,.62,1)
        _Wilted ("Вялая: кончики", Color) = (.47,.33,.15,1)
        _WiltedBase ("Вялая: у корня", Color) = (.22,.15,.07,1)
        _Cutoff ("Порог альфы", Range(0,1)) = .42
        _AgeSeconds ("Возраст от падения, с", Float) = 0
        _Opacity ("Видимость", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // Тень берём из карты теней: экранной в проекте нет, а пучки рисуются после неё.
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Fresh, _Wilted, _WiltedBase;
                float _Cutoff, _AgeSeconds, _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 root : TEXCOORD1;
                float4 bend : TEXCOORD2;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4 tint : TEXCOORD2;
                half2 state : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float age = max(0, _AgeSeconds);
                float3 root = input.root.xyz;
                float height = max(.01, input.root.w);
                float3 offset = input.positionOS.xyz - root;
                float along = saturate(offset.y / height);
                float wilt = smoothstep(input.bend.z, input.bend.z + .9, age);
                // Шлепок приминает пучок, дальше он вянет и ложится наружу от лужи.
                float appear = smoothstep(0, .12, age);
                offset.y *= lerp(1, .42, wilt * along) * lerp(.55, 1, appear);
                offset.xz += input.bend.xy * height * along * along * (wilt * .55 + (1 - appear) * .2);
                // Уходит вместе с коркой: сохнет и оседает.
                offset *= lerp(.15, 1, _Opacity);
                float3 position = root + offset;
                output.positionWS = TransformObjectToWorld(position);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.tint = input.color;
                output.state = half2(wilt, along);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(texel.a - _Cutoff - (1 - _Opacity) * .45);
                half luminance = dot(texel.rgb, half3(.3, .59, .11));
                half3 fresh = texel.rgb * _Fresh.rgb;
                half3 wilted = lerp(_WiltedBase.rgb, _Wilted.rgb, input.state.y) * (.55 + luminance * 1.6);
                half3 color = lerp(fresh, wilted, input.state.x) * input.tint.rgb;
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 sun = light.color / max(1e-3, max(light.color.r, max(light.color.g, light.color.b)));
                color *= lerp(.55, 1, light.shadowAttenuation) * lerp(half3(1, 1, 1), sun, .3);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
