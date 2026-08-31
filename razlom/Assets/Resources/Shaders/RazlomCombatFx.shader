Shader "Razlom/CombatFx"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RadialMask ("Radial Mask", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent"
               "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha One

        Pass
        {
            Name "CombatFx"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _RadialMask;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 sample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // Runtime kill glows are camera-facing quads.  A second mask
                // in the shader makes their edge mathematically transparent,
                // even after bilinear filtering and HDR additive blending.
                // Other combat sprites keep their authored alpha untouched.
                half radius = length(input.uv - half2(0.5h, 0.5h)) * 2.0h;
                half radial = 1.0h - smoothstep(0.62h, 0.82h, radius);
                half mask = lerp(1.0h, radial, saturate(_RadialMask));
                sample.rgb *= mask;
                sample.a *= mask;
                clip(sample.a - 0.001h);
                return sample * _Color;
            }
            ENDHLSL
        }
    }
}
