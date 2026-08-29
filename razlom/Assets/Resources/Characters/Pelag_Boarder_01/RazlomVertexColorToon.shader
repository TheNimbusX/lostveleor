Shader "Razlom/VertexColorToon"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _ToonSteps ("Toon Steps", Range(2,5)) = 3
        _OutlineColor ("Outline Color", Color) = (0.02,0.02,0.025,1)
        _OutlineWidth ("Outline Width", Range(0.0005,0.02)) = 0.004
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float3 normal:TEXCOORD0; float4 color:COLOR; };
            float4 _Tint;
            float _ToonSteps;
            v2f vert(appdata v)
            {
                v2f o;
                o.pos=UnityObjectToClipPos(v.vertex);
                o.normal=UnityObjectToWorldNormal(v.normal);
                o.color=v.color*_Tint;
                return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float3 lightDir=normalize(float3(-0.45,0.75,-0.55));
                float ndl=saturate(dot(normalize(i.normal),lightDir))*.72+.28;
                float shade=floor(ndl*_ToonSteps)/max(1,_ToonSteps-1);
                shade=max(.28,shade);
                return float4(i.color.rgb*shade,i.color.a);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; };
            struct v2f { float4 pos:SV_POSITION; };
            float4 _OutlineColor;
            float _OutlineWidth;
            v2f vert(appdata v)
            {
                v2f o;
                float3 expanded=v.vertex.xyz+normalize(v.normal)*_OutlineWidth;
                o.pos=UnityObjectToClipPos(float4(expanded,1));
                return o;
            }
            float4 frag(v2f i):SV_Target { return _OutlineColor; }
            ENDHLSL
        }
    }
    Fallback "Unlit/Color"
}
