Shader "Razlom/Forest Fruit Debris"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 p:POSITION; float3 n:NORMAL; float4 c:COLOR; };
            struct V { float4 p:SV_POSITION; float3 n:TEXCOORD0; float3 w:TEXCOORD1; float4 c:COLOR; };
            V vert(A a) { V v; v.w=TransformObjectToWorld(a.p.xyz); v.p=TransformWorldToHClip(v.w); v.n=TransformObjectToWorldNormal(a.n); v.c=a.c; return v; }
            half4 frag(V v):SV_Target
            {
                float3 n=normalize(v.n), eye=GetWorldSpaceNormalizeViewDir(v.w);
                float3 key=normalize(float3(-.4,1,-.3));
                float light=dot(n,key);
                float shade=lerp(.48,1.15,smoothstep(.12,.36,light));
                float ink=lerp(.55,1,smoothstep(.06,.25,abs(dot(n,eye))));
                float spec=pow(saturate(dot(n,normalize(key+eye))),24)*.085;
                return half4(v.c.rgb*shade*ink + float3(1,.55,.15)*spec,v.c.a);
            }
            ENDHLSL
        }
    }
}
