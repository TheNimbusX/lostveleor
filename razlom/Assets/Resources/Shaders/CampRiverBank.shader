Shader "Game/Camp River Bank"
{
    Properties
    {
        _Earth("Earth",2D)="white"{}
        _Turf("Turf",2D)="white"{}
        _BaseColor("Tint",Color)=(1,1,1,1)
        _WaterLevel("Water level",Float)=-.55
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode"="UniversalForward"}
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_Earth);SAMPLER(sampler_Earth);
            TEXTURE2D(_Turf);SAMPLER(sampler_Turf);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _WaterLevel;
            CBUFFER_END
            struct A {float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;};
            struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;half fog:TEXCOORD3;float4 shadow:TEXCOORD4;};
            V Vert(A v){V o;VertexPositionInputs p=GetVertexPositionInputs(v.p.xyz);o.p=p.positionCS;o.world=p.positionWS;o.normal=TransformObjectToWorldNormal(v.n);o.uv=v.uv;o.fog=ComputeFogFactor(o.p.z);o.shadow=GetShadowCoord(p);return o;}
            half4 Frag(V v):SV_Target
            {
                float2 p=v.world.xz;
                half3 rawEarth=SAMPLE_TEXTURE2D(_Earth,sampler_Earth,p/3.8).rgb;
                half3 earth=lerp(dot(rawEarth,half3(.2126,.7152,.0722)).xxx,rawEarth,.55)*half3(1.05,1.04,.96);
                half3 grass=SAMPLE_TEXTURE2D(_Turf,sampler_Turf,p/5.2).rgb*half3(.89,.97,.86);
                float noise=sin(p.x*6.7+sin(p.y*4.1))*sin(p.y*8.3+p.x*2.3);
                // Разметка меша сохраняет травяной край, когда берег вручную двигают по высоте.
                float wet=saturate(v.uv.y);
                float shore=smoothstep(.17,.76,wet+noise*.10);
                half3 color=lerp(grass,earth,shore)*lerp(1,.91,smoothstep(.80,1,wet));
                InputData input=(InputData)0;input.positionWS=v.world;input.normalWS=normalize(v.normal);
                input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(v.world);input.shadowCoord=v.shadow;
                input.bakedGI=SampleSH(input.normalWS)*.55;input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(v.p);input.shadowMask=1;
                SurfaceData surface=(SurfaceData)0;surface.albedo=color*_BaseColor.rgb;surface.alpha=1;surface.occlusion=1;surface.normalTS=half3(0,0,1);
                half4 lit=UniversalFragmentPBR(input,surface);lit.rgb=MixFog(lit.rgb,v.fog);return lit;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
