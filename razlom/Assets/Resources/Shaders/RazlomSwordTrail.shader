Shader "Razlom/SwordTrail"
{
    Properties
    {
        _Glow ("HDR Glow", Range(0,4)) = 1.55
        _Brush ("Brush breakup", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent+40" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Sword Trail"
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half _Glow;
                half _Brush;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half historyFade = smoothstep(0.02h, 0.30h, input.uv.x);
                half innerFeather = smoothstep(0.03h, 0.24h, input.uv.y);
                half outerFeather = 1.0h - smoothstep(0.82h, 1.0h, input.uv.y);
                half strokeShape = innerFeather * outerFeather;
                half bladeCore = lerp(0.74h, 1.34h,
                    smoothstep(0.30h, 0.84h, input.uv.y));
                half alpha = saturate(input.color.a * historyFade * strokeShape);
                half bristles = .68h + .32h * sin(input.uv.y * 57.0h + sin(input.uv.x * 12.0h) * 1.8h);
                half pigment = smoothstep(.08h, .42h, bristles + input.uv.x * .25h);
                alpha *= lerp(1.0h, pigment, _Brush);
                half3 warmGlow = input.color.rgb * (_Glow * bladeCore);
                return half4(warmGlow, alpha);
            }
            ENDHLSL
        }
    }
}
