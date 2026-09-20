Shader "Razlom/Pelag Forged Metal"
{
    Properties { _BaseColor("Tint",Color)=(1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct A { float4 p:POSITION;float3 n:NORMAL;half4 c:COLOR; };
            struct V { float4 p:SV_POSITION;float3 n:TEXCOORD0;float3 w:TEXCOORD1;half4 c:COLOR; };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            V vert(A a){V o;o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(a.n);o.c=a.c*_BaseColor;return o;}
            half4 frag(V i):SV_Target
            {
                Light l=GetMainLight(TransformWorldToShadowCoord(i.w));half3 n=normalize(i.n);
                half d=saturate(dot(n,l.direction));half shade=lerp(.55,1.18,smoothstep(.12,.7,d)*lerp(.6,1,l.shadowAttenuation));
                half rim=pow(1-saturate(dot(n,normalize(GetWorldSpaceViewDir(i.w)))),3)*.10;
                return half4(i.c.rgb*shade*l.color+rim*half3(.32,.39,.41),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
