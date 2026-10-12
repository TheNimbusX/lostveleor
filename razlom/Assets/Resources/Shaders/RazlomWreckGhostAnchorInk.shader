Shader "Razlom/Wreck Ghost Anchor Ink"
{
    // ЧЕРНИЛЬНЫЙ ОБВОД Призрачного якоря (07.10, к «Razlom/Wreck Ghost Anchor»): оболочка меша, раздутая по СГЛАЖЕННЫМ
    // нормалям (TEXCOORD1 копии меша — у Tripo нормали рвутся по швам, по ним оболочка дала бы щели) на постоянную ширину
    // в пикселях; задние грани; рисуется после тела и только там, где тело не записало трафарет 77, — снаружи силуэта.
    // Срез проявления и распада — тот же, что у тела (поле по исходной точке меша), иначе дыры распада залились бы чернилами.
    Properties
    {
        _Ink ("Ink", Color) = (.024,.043,.133,1)
        _OutlinePx ("Outline width, px", Range(0,8)) = 3
        _FacetTex ("Facet noise (r)", 2D) = "gray" {}
        _NoiseScale ("Noise scale (fine xy, coarse xy) per mesh unit", Vector) = (2.2,3.2,1.1,1.4)
        _Appear ("Appear 0..1 (controller)", Range(0,1)) = 1
        _Dissolve ("Dissolve 0..1, bottom first (controller)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+37" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Wreck Ghost Anchor Ink"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Front
            Stencil { Ref 77 Comp NotEqual Pass Keep }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 smoothOS : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
            half4 _Ink;
            float4 _FacetTex_ST, _NoiseScale;
            float _OutlinePx, _Appear, _Dissolve;
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
                cs.xy += dir * (_OutlinePx * 2.0 / _ScreenParams.xy) * cs.w;
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
                return half4(_Ink.rgb * _Ink.a, _Ink.a);
            }
            ENDHLSL
        }
    }
}
