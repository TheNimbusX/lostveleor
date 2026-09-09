Shader "Razlom/Leap Hovl"
{
    Properties
    {
        [MainTexture] _MainTex ("Hovl mask", 2D) = "white" {}
        _BaseColor ("Tail color", Color) = (.7,.12,.08,1)
        _CoreColor ("Ivory edge", Color) = (1,.91,.76,1)
        _Emission ("Brightness", Range(0,4)) = 2.3
        _Opacity ("Opacity", Range(0,1)) = 1
        _Phase ("Dissolve", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+31" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _CoreColor;
                float _Emission, _Opacity, _Phase;
            CBUFFER_END
            Varyings Vert(Attributes i)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float ink=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).r;
                float erosion=smoothstep(.3,1,_Phase)*.75;
                float aa=max(fwidth(ink),.025);
                float alpha=smoothstep(erosion,erosion+aa+.1,ink)*ink*_Opacity;
                half3 color=lerp(_BaseColor.rgb,_CoreColor.rgb*_Emission,smoothstep(.08,.5,ink));
                return half4(color*alpha,alpha);
            }
            ENDHLSL
        }
    }
}
