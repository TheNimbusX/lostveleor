Shader "Razlom/Meadow Ground"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _GrassTint ("Grass tint", Color) = (1,1,1,1)
        _Rounded ("Blend trail edges", Float) = 0
        _BaseMap ("Grass", 2D) = "white" {}
        _DirtMap ("Earth", 2D) = "grey" {}
        _Tiling ("World tiling", Float) = 0.35
        _Earth ("Earth coverage", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_DirtMap); SAMPLER(sampler_DirtMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _GrassTint;
                float _Tiling, _Earth, _Rounded;
            CBUFFER_END
            float4 _RazlomHeroLightPosition;
            half4 _RazlomHeroLightColor;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; half3 normal : TEXCOORD1; half fog : TEXCOORD2; float2 local : TEXCOORD3; float2 size : TEXCOORD4; };
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            Varyings Vert(Attributes input)
            {
                Varyings o; o.world=TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.world); o.normal=TransformObjectToWorldNormal(input.normalOS);
                o.fog=ComputeFogFactor(o.positionCS.z);
                o.size=float2(length(unity_ObjectToWorld._m00_m10_m20),length(unity_ObjectToWorld._m02_m12_m22));
                o.local=input.positionOS.xz*o.size; return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 p=i.world.xz;
                float broad=Noise(p*.11), detail=Noise(p*1.8);
                half3 grass=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,p*_Tiling).rgb;
                // UNS terrain colours are intentionally flat; reduce saturation and add continuous tonal variation.
                half luminance=dot(grass,half3(.22,.7,.08));
                grass=lerp(luminance.xxx,grass,.52)*half3(1.12,1.03,.9);
                half3 earth=SAMPLE_TEXTURE2D(_DirtMap,sampler_DirtMap,p*_Tiling).rgb;
                float patches=smoothstep(.53,.78,broad+.13*Noise(p*.38));
                float roundRadius=min(i.size.x,i.size.y)*.35;
                float2 q=abs(i.local)-(i.size*.5-roundRadius);
                float edgeDistance=length(max(q,0))+min(max(q.x,q.y),0)-roundRadius;
                float edge=smoothstep(.02,.3,-edgeDistance+(detail-.5)*.16);
                float coverage=lerp(_Earth,_Earth*edge,_Rounded);
                half3 albedo=lerp(grass*_GrassTint.rgb,earth*_BaseColor.rgb,saturate(patches*.42+coverage));
                albedo*=lerp(.8,1.22,broad)*lerp(.94,1.06,detail);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
                half3 normal=normalize(i.normal);
                half3 illumination=max(SampleSH(normal),half3(.2,.23,.19))+
                    light.color*saturate(dot(normal,light.direction))*lerp(.3,1,light.shadowAttenuation);
                half3 color=albedo*illumination;
                half hero=saturate(1-distance(i.world,_RazlomHeroLightPosition.xyz)/max(.001,_RazlomHeroLightPosition.w));
                color+=_RazlomHeroLightColor.rgb*hero*hero*.25;
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
