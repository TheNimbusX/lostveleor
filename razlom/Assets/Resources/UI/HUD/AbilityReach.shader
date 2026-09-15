Shader "Hidden/Razlom/AbilityReach"
{
    Properties { _Tint ("Tint", Color) = (1,.91,.67,.78) }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off ZTest LEqual
            Offset -1,-1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            half4 _Tint;
            Varyings vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; return o; }
            half4 frag(Varyings i):SV_Target
            {
                float edge=abs(i.uv.y-.5);
                float aa=max(fwidth(edge),.06);
                float alpha=1-smoothstep(.30-aa,.50,edge);
                return half4(_Tint.rgb,_Tint.a*alpha);
            }
            ENDHLSL
        }
    }
}
