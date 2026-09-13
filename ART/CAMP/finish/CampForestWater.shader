Shader "Game/Camp Forest Water"
{
    Properties
    {
        _DeepWaterColor("Цвет течения",Color)=(.12,.35,.38,1)
        _ShallowWaterColor("Цвет у берега",Color)=(.3,.54,.43,1)
        _FoamColor("Светлая рябь",Color)=(.63,.80,.73,1)
        [Normal] _Normal01("Речная рябь",2D)="bump"{}
        _FlowSpeed("Скорость течения",Range(0,2))=.3
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
            CBUFFER_START(UnityPerMaterial)
                half4 _DeepWaterColor,_ShallowWaterColor,_FoamColor;
                float _FlowSpeed;
            CBUFFER_END
            float4 _CampBreeze;
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
                float shore=(1-smoothstep(.008,.023,edge+normal.x*.003))*(.48+.15*normal.y);
                color=lerp(color,_FoamColor.rgb,saturate(crest+shore));
                Light light=GetMainLight(v.shadow);
                color*=lerp(.74,1,light.shadowAttenuation);
                return half4(MixFog(color,v.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
