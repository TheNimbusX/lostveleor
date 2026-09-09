Shader "Razlom/Leap Stroke"
{
    Properties
    {
        [MainColor] _BaseColor ("Sash red", Color) = (.55,.09,.11,1)
        _CoreColor ("Warm ivory", Color) = (1,.88,.67,1)
        _Emission ("Core brightness", Range(0,4)) = 1.8
        _Opacity ("Opacity", Range(0,1)) = 1
        _Phase ("Wave phase", Float) = 0
        _CoreWidth ("Rounded highlight width", Range(.03,.25)) = .07
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
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _CoreColor;
            half _Emission, _Opacity, _CoreWidth;
            float _Phase;
            CBUFFER_END
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.uv=v.uv; o.color=v.color; return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float x=saturate(i.uv.x), y=i.uv.y;
                float flow=_Time.y*5.5+_Phase*2;
                float bend=.035*sin(x*8-flow)+.018*sin(x*15-flow*1.3);
                float d=abs(y-.5-bend);
                float aa=max(fwidth(d)*1.5,.008);
                float taper=pow(saturate(sin(x*3.14159265)),.35);
                float halfWidth=.36*taper;
                float edge=1-smoothstep(halfWidth-aa,halfWidth+aa,d);
                float roundness=sqrt(saturate(1-pow(d/max(.001,halfWidth),2)));
                // Широкий полутон создаёт округлую массу; узкий блик движется по ней.
                float highlight=exp2(-pow((y-(.37+bend))/_CoreWidth,2));
                float vein=exp2(-600*pow(y-(.63+bend),2));
                float stream=.84+.16*sin(x*17-flow*2+y*3);
                float ends=smoothstep(0,.07,x)*(1-smoothstep(.78,1,x));
                float alpha=edge*ends*(.32+.54*roundness)*stream*i.color.a*_Opacity*_BaseColor.a;
                half3 body=lerp(_BaseColor.rgb*.65,_BaseColor.rgb*1.3,roundness);
                half3 rgb=lerp(body,_CoreColor.rgb*_Emission,highlight*.92);
                rgb+=_CoreColor.rgb*vein*.22;
                return half4(rgb*i.color.rgb*alpha,alpha);
            }
            ENDHLSL
        }
    }
}
