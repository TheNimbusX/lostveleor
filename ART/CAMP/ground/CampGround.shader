Shader "Game/Camp Ground"
{
    Properties
    {
        _BaseMap ("Existing path footprint", 2D) = "white" {}
        _GrassTex ("Meadow albedo", 2D) = "white" {}
        _DirtTex ("Worn earth albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _TileMeters ("Texture size in metres", Float) = 3
        _AmbientStrength ("Ground ambient light", Range(0,1)) = 0.55
        _StoneRelief ("Embedded stone relief metres", Range(0,0.04)) = 0.018
        _IsPath ("Path overlay", Float) = 0
        _IsSurface ("Continuous surface", Float) = 0
        _SurfaceMap ("Path, stones, contact, turf map", 2D) = "white" {}
        _StonyTex ("Embedded stones", 2D) = "white" {}
        _TurfTex ("Leafy turf", 2D) = "white" {}
        _SurfaceBounds ("World bounds", Vector) = (0,0,30,30)
        _SrcBlend ("Source blend", Float) = 1
        _DstBlend ("Destination blend", Float) = 0
        _ZWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
            TEXTURE2D(_DirtTex); SAMPLER(sampler_DirtTex);
            TEXTURE2D(_SurfaceMap); SAMPLER(sampler_SurfaceMap);
            TEXTURE2D(_StonyTex); SAMPLER(sampler_StonyTex);
            TEXTURE2D(_TurfTex); SAMPLER(sampler_TurfTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _TileMeters, _IsPath, _SrcBlend, _DstBlend, _ZWrite;
                float _IsSurface;float4 _SurfaceBounds;
                half _AmbientStrength;float _StoneRelief;
            CBUFFER_END
            float4 _CampStudyArea;
            float _CampShadowDiagnostic;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 uv : TEXCOORD1; half fog : TEXCOORD2; float4 shadowCoord : TEXCOORD3; half3 normalWS:TEXCOORD4; };
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS;
                o.shadowCoord=GetShadowCoord(p);
                o.normalWS=_IsSurface>.5?TransformObjectToWorldNormal(v.normalOS):half3(0,1,0);
                o.uv=TRANSFORM_TEX(v.uv,_BaseMap); o.fog=ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                // Мировые координаты связывают повёрнутые пятна в одну тропу.
                float2 p=i.positionWS.xz;
                float2 uv=p/max(_TileMeters,.1);
                half3 grass=SAMPLE_TEXTURE2D(_GrassTex,sampler_GrassTex,p/8).rgb;
                half3 dirt=SAMPLE_TEXTURE2D(_DirtTex,sampler_DirtTex,uv).rgb;
                grass=lerp(dot(grass,half3(.2126,.7152,.0722)).xxx,grass,.90)*half3(.60,.82,.68);
                dirt=lerp(dot(dirt,half3(.2126,.7152,.0722)).xxx,dirt,.70)*half3(.92,.86,.78);
                float macro=Noise(p*.28);
                grass*=lerp(half3(.79,.87,.83),half3(1.06,1.03,.92),macro);
                dirt*=lerp(.92,1.04,Noise(p*.65));
                grass=lerp(grass,dirt,smoothstep(.58,.85,Noise(p*.7+19))*.14);
                half alpha=1;
                half3 albedo=grass;
                half occlusion=1;
                float reliefHeight=0;
                float study=(1-smoothstep(_CampStudyArea.z-1.2,_CampStudyArea.z,distance(p,_CampStudyArea.xy)))*_CampStudyArea.w;
                float islands=Noise(p*1.35)*.7+Noise(p*3.8)*.3;
                half3 quietGrass=grass;
                half3 meadow=grass*lerp(.94,1.04,Noise(p*.43));
                albedo=lerp(albedo,meadow,study);
                if(_IsPath>.5)
                {
                    float footprint=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a;
                    // Сохраняем авторский контур; мелкая неоднородность разрывает мягкий край.
                    float edgeNoise=Noise(p*9)*.65+Noise(p*23)*.35;
                    alpha=smoothstep(.20,.54,footprint+(edgeNoise-.5)*.30);
                    float fringe=1-smoothstep(.22,.64,footprint);
                    albedo=lerp(dirt,grass,fringe*.48);
                    float incursions=smoothstep(.48,.69,islands)*(1-smoothstep(.35,.82,footprint));
                    alpha*=1-incursions*study*.60;
                    albedo=lerp(albedo,lerp(dirt,quietGrass,incursions*.72),study);
                    clip(alpha-.005);
                }
                if(_IsSurface>.5)
                {
                    float2 mapUV=(p-_SurfaceBounds.xy)/_SurfaceBounds.zw;
                    half4 layers=SAMPLE_TEXTURE2D(_SurfaceMap,sampler_SurfaceMap,mapUV);
                    // Два поворота исходной живописной фактуры разрушают повтор диагональных рядов.
                    float variant=smoothstep(.36,.64,Noise(p*.49+37));
                    float2 rotated=float2(p.y,-p.x);
                    half3 turfA=SAMPLE_TEXTURE2D(_TurfTex,sampler_TurfTex,p/5.2).rgb;
                    half3 turfB=SAMPLE_TEXTURE2D(_TurfTex,sampler_TurfTex,rotated/4.7+float2(.37,.61)).rgb;
                    half3 turf=lerp(turfA,turfB,variant)*half3(.89,.97,.86);
                    grass=lerp(grass,turf,lerp(.64,.92,layers.a));
                    half3 stoneA=SAMPLE_TEXTURE2D(_StonyTex,sampler_StonyTex,p/3.4).rgb;
                    half3 stoneB=SAMPLE_TEXTURE2D(_StonyTex,sampler_StonyTex,rotated/3.1+float2(.21,.46)).rgb;
                    // Узкая переходная зона сохраняет нарисованные грани камней без двойного изображения.
                    half3 stony=lerp(stoneA,stoneB,smoothstep(.46,.54,Noise(p*.32+11)));
                    // Нейтральные плоскости камня отделяются от коричневой почвы по цвету, а не по яркости света.
                    float stoneShape=smoothstep(.38,.68,stony.b/max(stony.r,.001));
                    reliefHeight=stoneShape*_StoneRelief*layers.g*smoothstep(.25,.85,layers.r);
                    stony=lerp(dot(stony,half3(.2126,.7152,.0722)).xxx,stony,.78)*half3(.96,1,1.03);
                    half3 worn=dirt*lerp(half3(.79,.73,.63),half3(1.10,1.04,.95),smoothstep(.18,.82,Noise(p*.36+7)));
                    worn=lerp(worn,stony,layers.g);
                    float brokenEdge=layers.r+(Noise(p*14)-.5)*.26;
                    albedo=lerp(grass,worn,smoothstep(.30,.69,brokenEdge));
                    occlusion=layers.b;
                }
                InputData data=(InputData)0;
                data.positionWS=i.positionWS;
                // Ground_Base имеет нулевой масштаб Y, поэтому нормаль задаётся явно.
                data.normalWS=normalize(i.normalWS);
                if(_IsSurface>.5)
                {
                    float3 dx=ddx(i.positionWS),dy=ddy(i.positionWS);
                    float3 acrossX=cross(dy,data.normalWS),acrossY=cross(data.normalWS,dx);
                    float determinant=dot(dx,acrossX);
                    float3 gradient=ddx(reliefHeight)*acrossX+ddy(reliefHeight)*acrossY;
                    // Вырожденная экранная производная оставляет исходную нормаль.
                    float3 bumped=normalize(data.normalWS-sign(determinant)*gradient/max(abs(determinant),1e-8));
                    data.normalWS=normalize(lerp(data.normalWS,bumped,.75));
                }
                data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    data.shadowCoord=i.shadowCoord;
                #else
                    data.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
                #endif
                data.bakedGI=SampleSH(data.normalWS)*_AmbientStrength;
                data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
                data.shadowMask=half4(1,1,1,1);
                if(_CampShadowDiagnostic>.5)
                {
                    Light diagnosticLight=GetMainLight(data.shadowCoord,data.positionWS,data.shadowMask);
                    if(_CampShadowDiagnostic>1.5)
                    {
                        half3 direct=diagnosticLight.color*diagnosticLight.distanceAttenuation*diagnosticLight.shadowAttenuation*saturate(dot(data.normalWS,diagnosticLight.direction));
                        return half4(albedo*(direct+data.bakedGI*occlusion),1);
                    }
                    return half4(diagnosticLight.shadowAttenuation.xxx,1);
                }
                SurfaceData surface=(SurfaceData)0;
                surface.albedo=albedo*_BaseColor.rgb;
                surface.normalTS=half3(0,0,1);
                surface.occlusion=occlusion; surface.alpha=alpha;
                half4 color=UniversalFragmentPBR(data,surface);
                color.rgb=MixFog(color.rgb,i.fog); color.a=alpha;
                return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
