Shader "Game/Camp Rift Glow"
{
    Properties
    {
        _Color("Цвет свечения", Color) = (1,.46,.075,1)
        _Intensity("Яркость лучей", Float) = 1
        _Phase("Фаза луча", Float) = 0
        _FlowTime("Время движения света", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Intensity;
            float _Phase;
            float _FlowTime;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 directionWS = -GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 originWS = input.positionWS - directionWS * dot(input.positionWS - GetCameraPositionWS(), directionWS);
                float3 origin = TransformWorldToObject(originWS);
                float3 direction = mul((float3x3)GetWorldToObjectMatrix(), directionWS);
                float3 inverse = rcp(direction + 0.000001);
                float3 a = (-.5 - origin) * inverse;
                float3 b = (.5 - origin) * inverse;
                float3 low = min(a,b), high = max(a,b);
                float entry = max(0, max(low.x,max(low.y,low.z)));
                float exit = min(high.x,min(high.y,high.z));

                // Глубина сцены обрезает объём настоящими столбами и телом героя.
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float depth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif
                float3 surface = ComputeWorldSpacePosition(uv,depth,UNITY_MATRIX_I_VP);
                exit = min(exit, dot(surface - originWS,directionWS));
                float segment = max(0,exit-entry) / 16;
                float seed = _Phase;
                float glow = 0;
                [unroll] for (int i=0;i<16;i++)
                {
                    float3 p = origin + direction * (entry + (i+.5) * segment);
                    float h = p.y + .5;
                    float2 drift = float2(sin(h*5 + seed - _FlowTime*1.1), cos(h*4 - seed - _FlowTime*.8)) * .12;
                    float2 across = p.xz - drift;
                    float spread = lerp(.22,.40,h);
                    float density = exp2(-dot(across,across) / (spread*spread) * 5);
                    float ends = smoothstep(0,.18,h) * (1-smoothstep(.48,1,h));
                    float flow = .35 + .65 * pow(.5+.5*sin(h*10 - _FlowTime*2.5 + seed),2);
                    glow += density * ends * flow * segment;
                }
                float pulse = .8 + .2 * sin(_FlowTime*.9 + seed);
                float intensity = (1-exp2(-glow * 5)) * pulse * _Intensity;
                float tint = .5+.5*sin(seed + _FlowTime*.5);
                return half4(lerp(_Color.rgb, _Color.rgb * float3(1,1.4,.65),tint*.4) * intensity * 1.8,0);
            }
            ENDHLSL
        }
    }
}
