Shader "Razlom/ComicSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OutlineColor ("Ink", Color) = (0.025,0.035,0.06,1)
        _OutlinePixels ("Ink Width", Range(0,3)) = 1.15
        _Flash ("Hit Flash", Range(0,1)) = 0
        _Dissolve ("Dissolve", Range(0,1)) = 0
        _DissolveColor ("Dissolve Edge", Color) = (1,0.45,0.12,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ComicSprite"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float2 objectUV : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _OutlineColor;
                half4 _DissolveColor;
                half _OutlinePixels;
                half _Flash;
                half _Dissolve;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.objectUV = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 stepUV = _MainTex_TexelSize.xy * _OutlinePixels;
                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;

                half around = 0;
                around = max(around, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2( stepUV.x, 0)).a);
                around = max(around, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(-stepUV.x, 0)).a);
                around = max(around, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(0,  stepUV.y)).a);
                around = max(around, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(0, -stepUV.y)).a);

                float noise = hash21(floor(input.objectUV * 96.0));
                float dissolveMask = saturate((1.0 - _Dissolve) * 1.18 - noise);
                float edge = saturate(1.0 - abs(noise - (1.0 - _Dissolve)) * 18.0) * step(0.001, _Dissolve);

                half outlineAlpha = saturate(around - source.a) * dissolveMask;
                half alpha = max(source.a * dissolveMask, outlineAlpha);
                clip(alpha - 0.01);

                half3 rgb = lerp(_OutlineColor.rgb, source.rgb, source.a);
                rgb = lerp(rgb, half3(1.0, 0.96, 0.86), _Flash * source.a);
                rgb = lerp(rgb, _DissolveColor.rgb, edge * source.a);
                return half4(rgb, alpha * input.color.a);
            }
            ENDHLSL
        }
    }
}
