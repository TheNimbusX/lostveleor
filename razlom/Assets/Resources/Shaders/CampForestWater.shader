Shader "Game/Camp Forest Water"
{
    Properties
    {
        _DeepWaterColor("Цвет течения",Color)=(.12,.35,.38,1)
        _ShallowWaterColor("Цвет у берега",Color)=(.3,.54,.43,1)
        _FoamColor("Светлая рябь",Color)=(.63,.80,.73,1)
        [Normal] _Normal01("Речная рябь",2D)="bump"{}
        _FlowSpeed("Скорость течения",Range(0,2))=.3
        [Toggle] _BridgePolishEnabled("Локальная доводка у моста",Float)=0
        [NoScaleOffset] _BridgeDepthMap("Глубина / препятствия / берег / область",2D)="black"{}
        _BridgeDepthBounds("Область карты XZ",Vector)=(0,0,1,1)
        _BridgeDeepColor("Глубокая вода у моста",Color)=(.10,.29,.31,1)
        _BridgeShallowColor("Мелководье у моста",Color)=(.24,.36,.29,1)
        _BridgeSkyColor("Оттенок неба",Color)=(.42,.56,.65,1)
        _BridgeSunStrength("Мягкий солнечный блик",Range(0,.3))=.07
        _BridgeFoamStrength("Пена у камней и опор",Range(0,.5))=.16
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+10"}
        Pass
        {
            Tags {"LightMode"="UniversalForward"}
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_Normal01);SAMPLER(sampler_Normal01);
            TEXTURE2D(_BridgeDepthMap);SAMPLER(sampler_BridgeDepthMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _DeepWaterColor,_ShallowWaterColor,_FoamColor;
                float _FlowSpeed;
                float4 _BridgeDepthBounds;
                half4 _BridgeDeepColor,_BridgeShallowColor,_BridgeSkyColor;
                float _BridgePolishEnabled,_BridgeSunStrength,_BridgeFoamStrength;
            CBUFFER_END
            float4 _CampBreeze;
            float _CampDepthOn; float4 _CampDepthShore;
            struct A {float4 p:POSITION;float2 uv:TEXCOORD0;};
            struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;float4 shadow:TEXCOORD2;half fog:TEXCOORD3;};
            V Vert(A v){V o;VertexPositionInputs p=GetVertexPositionInputs(v.p.xyz);o.p=p.positionCS;o.uv=v.uv;o.world=p.positionWS;o.shadow=GetShadowCoord(p);o.fog=ComputeFogFactor(o.p.z);return o;}
            half4 Frag(V v):SV_Target
            {
                float time=_CampBreeze.w*_FlowSpeed;
                float2 uv=float2(v.uv.x*1.1-time*.045,v.uv.y*1.3);
                half3 normal=UnpackNormal(SAMPLE_TEXTURE2D(_Normal01,sampler_Normal01,uv));
                half3 second=UnpackNormal(SAMPLE_TEXTURE2D(_Normal01,sampler_Normal01,uv*.57+float2(-time*.018,.37)));
                float edge=min(v.uv.y,1-v.uv.y);
                float shallow=1-smoothstep(.025,.28,edge+normal.y*.009);
                half3 color=lerp(_DeepWaterColor.rgb,_ShallowWaterColor.rgb,shallow*.78);
                color*=1+(normal.x+second.y)*.075;
                // Короткие широкие мазки дают читаемое течение без мелкого фотографического шума.
                float crest=smoothstep(.34,.52,normal.y)*smoothstep(.08,.29,second.x)*.25;
                float shore=(1-smoothstep(.006,.05,edge+normal.x*.003))*(.32+.10*normal.y);
                // Разрывы привязаны к берегу; течение и пена у опор сохраняют прежний рисунок.
                if(_CampDepthOn>.5)
                {
                    float patches=sin(v.world.x*.51+v.world.z*.33)*.5+.5;
                    float fine=sin(v.world.x*1.17-v.world.z*.71)*.5+.5;
                    float broken=smoothstep(.26,.72,patches*.75+fine*.25);
                    shore*=lerp(1,lerp(.12,1,broken),_CampDepthShore.x)*_CampDepthShore.y;
                }
                color=lerp(color,_FoamColor.rgb,saturate(crest+shore));
                Light light=GetMainLight(v.shadow);
                if(_BridgePolishEnabled>.5)
                {
                    float2 mapUV=(v.world.xz-_BridgeDepthBounds.xy)/max(_BridgeDepthBounds.zw,float2(.001,.001));
                    half4 bed=SAMPLE_TEXTURE2D(_BridgeDepthMap,sampler_BridgeDepthMap,mapUV);
                    float inBounds=step(0,mapUV.x)*step(mapUV.x,1)*step(0,mapUV.y)*step(mapUV.y,1);
                    float blend=bed.a*inBounds;
                    // R is actual baked world-space depth in metres, not distance across the ribbon.
                    float depth=smoothstep(.035,.53,bed.r);
                    half3 localColor=lerp(_BridgeShallowColor.rgb,_BridgeDeepColor.rgb,depth);
                    localColor*=1+(normal.x+second.y)*.045;
                    float broken=smoothstep(.12,.58,second.x*.5+normal.y*.3+.2);
                    float contactFoam=(bed.g*.75+bed.b*.20)*broken*_BridgeFoamStrength;
                    localColor=lerp(localColor,_FoamColor.rgb,saturate(crest*.60+contactFoam));
                    half3 surfaceNormal=normalize(half3(normal.x*.16+second.x*.08,1,normal.y*.16+second.y*.08));
                    half3 view=GetWorldSpaceNormalizeViewDir(v.world);
                    half3 halfway=SafeNormalize(light.direction+view);
                    float sun=pow(saturate(dot(surfaceNormal,halfway)),18)*_BridgeSunStrength;
                    float sky=pow(1-saturate(dot(surfaceNormal,view)),3)*.12;
                    localColor=lerp(localColor,_BridgeSkyColor.rgb,sky);
                    localColor+=light.color*sun*light.shadowAttenuation;
                    color=lerp(color,localColor,blend);
                }
                color*=lerp(.74,1,light.shadowAttenuation);
                return half4(MixFog(color,v.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
