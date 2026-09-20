Shader "Razlom/Forest Fruit"
{
    Properties
    {
        _BaseMap("Раскраска", 2D) = "white" {}
        _BaseColor("Цвет", Color) = (1,1,1,1)
        _Smoothness("Блик", Range(0,1)) = .4
        _BurstCharge("Давление мякоти", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _BaseColor;
                float _Smoothness, _BurstCharge;
            CBUFFER_END
            struct A { float4 p : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float3 n : TEXCOORD0; float3 w : TEXCOORD1; float2 uv : TEXCOORD2; float3 local : TEXCOORD3; };
            V vert(A a)
            {
                V v; v.w = TransformObjectToWorld(a.p.xyz); v.p = TransformWorldToHClip(v.w);
                v.n = TransformObjectToWorldNormal(a.n); v.uv = TRANSFORM_TEX(a.uv, _BaseMap); v.local=a.n; return v;
            }
            half4 frag(V v) : SV_Target
            {
                float3 n = normalize(v.n), eye = GetWorldSpaceNormalizeViewDir(v.w);
                float3 key = normalize(GetMainLight().direction + float3(-.25,.5,-.2));
                float light = dot(n, key);
                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, v.uv).rgb * _BaseColor.rgb;
                float3 tone = lerp(float3(.52,.32,.25), float3(1.30,1.17,.91), smoothstep(-.3,.8,light));
                float gloss = smoothstep(.6,.94,pow(saturate(dot(n,normalize(key+eye))),18));
                float rim = pow(1-saturate(dot(n,eye)),3) * saturate(n.y*.5+.5);
                float angle=atan2(v.local.y,v.local.x);
                float seam=abs(sin(angle*3 + sin(v.local.z*17)*.13));
                float cracks=1-smoothstep(.025,.10,seam);
                float rind=smoothstep(.02,.15,albedo.r-albedo.g)*smoothstep(.23,.5,albedo.r);
                float3 color=albedo*tone + float3(1,.77,.4)*gloss*.65 + float3(.3,.15,.035)*rim;
                color=lerp(color,float3(1.35,.63,.09),cracks*rind*_BurstCharge*.82);
                return half4(color, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
