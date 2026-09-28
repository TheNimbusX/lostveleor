Shader "Razlom/Forest Acid Surface"
{
    Properties
    {
        _BaseMap ("Painted viscous acid RGBA", 2D) = "white" {}
        _AgeSeconds ("Simulation age", Float) = 0
        _Opacity ("Visible fraction", Range(0,1)) = 1
        _Spread ("Spread", Range(.01,1)) = 1
        _Seed ("Puddle serial", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-5" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _AgeSeconds, _Opacity, _Spread, _Seed;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float age = max(0, _AgeSeconds);
                float phase = age * 2.8 + _Seed * .73;
                // Жидкость слегка поднимается волнами, оставаясь на рельефе.
                float swell = sin(input.uv.x * 13 + phase) * sin(input.uv.y * 11 - phase * .7);
                input.positionOS.y += .012 * (1 + swell) * saturate(age * 5);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 centered = uv * 2 - 1;
                float radius = length(centered);
                float inside = step(abs(centered.x), 1) * step(abs(centered.y), 1);
                float age = max(0, _AgeSeconds);
                float phase = age * 2.8 + _Seed * .73;
                // Фронт разливается от контакта отдельными языками. Текстура
                // не растягивается целиком, а открывается на неподвижной земле.
                float spreadTime = saturate(age / .30);
                float angle = atan2(centered.y, centered.x);
                float tongues = sin(angle * 3 + _Seed) * .12 + sin(angle * 7 - _Seed) * .07;
                float front = lerp(.025, 1.22, smoothstep(0, 1, spreadTime));
                front += tongues * (1 - spreadTime);
                float reveal = (1 - smoothstep(front - .055, front + .025, radius)) * saturate(age / .035);
                float2 drift = float2(sin(uv.y * 12 + phase), cos(uv.x * 10 - phase * .8));
                drift *= .018 * saturate(1.1 - radius);
                half4 painted = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + drift);
                half edgeAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + drift * .22).a;
                half acid = saturate((painted.g - painted.r * .65 - painted.b * .3) * 4);
                float flow = sin(uv.x * 13 + phase + sin(uv.y * 9 - phase * .4));
                float eddy = sin(uv.y * 11 - phase * .67 + sin(uv.x * 10 + phase));
                half3 normal = normalize(input.normalWS + half3(flow, 0, eddy) * .24);
                Light sun = GetMainLight();
                half diffuse = saturate(dot(normal, sun.direction));
                half3 view = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half specular = pow(saturate(dot(normal, SafeNormalize(sun.direction + view))), 28) * .24;
                half3 albedo = lerp(painted.rgb, half3(.21,.30,.027), .28);
                half3 color = albedo * (.45 + diffuse * .45) * lerp(half3(1,1,1), sun.color, .25);
                color *= 1 + flow * .07 * acid;
                // Движущиеся мокрые блики вместо постоянной светящейся заливки.
                color += half3(.70,.85,.30) * (specular + smoothstep(.68, .97, flow * eddy) * .07) * acid;
                float settling = exp2(-age * 6) * .08 * sin(radius * 27 - age * 22);
                color += half3(.40,.51,.12) * max(0, settling);
                // A thin wet film covers inward silhouette notches inside the
                // gameplay radius; the authored thick rim stays irregular.
                half film = (1 - smoothstep(.81, .845, radius)) * .16;
                half alpha = (edgeAlpha * .91 + film * (1 - edgeAlpha)) * _Opacity * inside * reveal;
                half3 premultiplied = color * edgeAlpha * .91 + half3(.25,.31,.025) * film * (1 - edgeAlpha);
                clip(alpha - .002);
                return half4(premultiplied * _Opacity * inside * reveal, alpha);
            }
            ENDHLSL
        }
    }
}
