Shader "Razlom/Forest Water"
{
    Properties
    {
        _DeepColor ("Deep water", Color) = (.12,.26,.27,1)
        _ShallowColor ("Shallow water", Color) = (.24,.32,.24,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor, _ShallowColor;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float2 uv:TEXCOORD1; half fog:TEXCOORD2; };
            V Vert(A v)
            {
                V o; o.world=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.world); o.uv=v.uv;
                o.fog=ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.world.xz;
                float ripple=sin(p.x*3.1+p.y*1.7+sin(p.y*.7)+_Time.y*.65)*.6
                    + sin(p.y*2.4-p.x*.9-_Time.y*.43)*.4;
                float edge=smoothstep(.48,1,length(i.uv));
                half3 normal=normalize(half3(ripple*.025,1,cos(p.x*2.1-p.y*2+_Time.y*.5)*.02));
                Light light=GetMainLight();
                half3 color=lerp(_DeepColor.rgb,_ShallowColor.rgb,edge);
                color*=max(SampleSH(normal),half3(.35,.38,.35))+light.color*saturate(dot(normal,light.direction))*.7;
                half3 view=GetWorldSpaceNormalizeViewDir(i.world);
                float glint=pow(saturate(dot(normal,normalize(light.direction+view))),64);
                color+=light.color*glint*.035 + ripple*.003;
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
