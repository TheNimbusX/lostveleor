Shader "Razlom/Forest Sap"
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
            struct V { float4 p:SV_POSITION; float3 n:TEXCOORD0; float4 c:COLOR; };
            V vert(A a) { V v; v.p=TransformObjectToHClip(a.p.xyz); v.n=TransformObjectToWorldNormal(a.n); v.c=a.c; return v; }
            half4 frag(V v):SV_Target
            {
                float light = saturate(dot(normalize(v.n),normalize(float3(-.4,1,-.3))));
                return half4(v.c.rgb*lerp(.5,1.4,light)+pow(light,12)*.3,v.c.a);
            }
            ENDHLSL
        }
    }
}
