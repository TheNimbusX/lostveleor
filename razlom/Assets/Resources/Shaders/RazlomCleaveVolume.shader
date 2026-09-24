Shader "Razlom/CleaveVolume"
{
    Properties
    {
        _MainTex ("Painted stroke", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent+39" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Cleave Volume"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

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
                half history = smoothstep(.02h, .20h, input.uv.x);
                half edge = lerp(.58h, 1.0h, smoothstep(.02h, .83h, input.uv.y));
                half pigment = .85h + .15h * sin(input.uv.y * 33.0h + input.uv.x * 47.0h);
                // The authored mask has transparent borders. Sampling only its
                // painted center lets the geometric blade edge stay bright.
                half4 paint = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex,
                    float2(input.uv.x, lerp(.37h, .63h, input.uv.y)));
                half alpha = saturate(input.color.a * history * edge * pigment * paint.a);
                // The existing brush is a shape mask. Its beige pigment must not
                // tint the steel palette supplied by the animated blade mesh.
                return half4(input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
