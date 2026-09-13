Shader "Game/Camp Biome Ground"
{
    Properties
    {
        _Detail("Фактура",2D)="gray"{}
        _Color("Цвет",Color)=(1,1,1,.8)
        _Kind("Снег / пепел / яд / мох",Float)=0
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent"}
        Pass
        {
            Tags {"LightMode"="UniversalForward"}
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_Detail);SAMPLER(sampler_Detail);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;float _Kind;
            CBUFFER_END
            float4 _CampBreeze;
            struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};
            struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;float4 shadow:TEXCOORD2;};
            V Vert(A a){V o;VertexPositionInputs p=GetVertexPositionInputs(a.p.xyz);o.p=p.positionCS;o.world=p.positionWS;o.shadow=GetShadowCoord(p);o.uv=a.uv;return o;}
            half4 Frag(V v):SV_Target
            {
                float2 p=v.uv*2-1;float r=length(p);
                float grain=SAMPLE_TEXTURE2D(_Detail,sampler_Detail,v.world.xz*1.3).r;
                float edge=r+(grain-.5)*.21+sin(atan2(p.y,p.x)*7+.3)*.045;
                float alpha=(1-smoothstep(.65,.95,edge))*_Color.a;
                half3 color=_Color.rgb*lerp(.84,1.12,grain);
                if(_Kind>1.5 && _Kind<2.5)
                {
                    alpha=(1-smoothstep(.80,.88,edge))*_Color.a;
                    float rim=smoothstep(.55,.76,edge)*(1-smoothstep(.76,.85,edge));
                    float rings=pow(saturate(sin(r*24-_CampBreeze.w*.65)),14)*.10;
                    color=lerp(color,color*1.45+half3(.05,.08,0),rim*.6+rings);
                }
                Light light=GetMainLight(v.shadow);
                color*=lerp(.68,1,light.shadowAttenuation);
                return half4(color,alpha);
            }
            ENDHLSL
        }
    }
}
