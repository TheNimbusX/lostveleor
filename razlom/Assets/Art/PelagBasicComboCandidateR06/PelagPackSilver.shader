Shader "Razlom/Candidate/Pelag Pack Silver" {
 Properties { _BaseMap("Pack texture",2D)="white"{} _BaseColor("Silver tint",Color)=(1,1,1,1) _Animated("Stroke motion",Float)=0 _Reverse("Reverse",Float)=0 _Downward("Downward cut",Float)=0 _Phase("Stroke age",Float)=0 }
 SubShader {
 Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
 Pass {
 Blend SrcAlpha OneMinusSrcAlpha
 ZWrite Off
 Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST;half4 _BaseColor;float _Animated,_Reverse,_Downward,_Phase;
 CBUFFER_END
 struct A {float4 p:POSITION;float3 uv:TEXCOORD0;half4 color:COLOR;};
 struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;float3 stroke:TEXCOORD1;};
 V vert(A a){V v;float age=_Phase;float phase=smoothstep(0,.28,age);float3 p=a.p.xyz;float u=saturate(atan2(p.x,p.z)/1.570796+.5);float f=saturate((length(p.xz)-.12)/2.08);
 if(_Animated>.5){p.xz*=.86+.14*phase;p.y+=sin((u-phase)*3.14159)*.10*(1-age);}
 v.p=TransformObjectToHClip(p);v.uv=a.uv.xy*_BaseMap_ST.xy+_BaseMap_ST.zw;v.color=a.color;v.stroke=float3(u,f,age);return v;}
 half4 frag(V v):SV_Target {
 half4 t=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,v.uv);
 half intensity=max(t.r,max(t.g,t.b));
 half vertexIntensity=max(v.color.r,max(v.color.g,v.color.b));
 float alpha=t.a*v.color.a*_BaseColor.a*smoothstep(.02,.30,intensity);
 if(_Animated>.5){
 float coordinate=lerp(v.stroke.x,1-v.stroke.x,_Reverse);
 coordinate=lerp(coordinate,v.stroke.y,_Downward);
 float sweep=1.17*smoothstep(0,.28,v.stroke.z);
 alpha*=1-smoothstep(sweep-.09,sweep+.13,coordinate);
 alpha*=.28+.72*pow(saturate(v.stroke.y),1.4);
 intensity*=.75+.55*smoothstep(.65,.95,v.stroke.y);
 }
 alpha=saturate(alpha);
 if(!(alpha>.003)) discard;
 return half4(intensity*vertexIntensity*_BaseColor.rgb,alpha);
 }
 ENDHLSL
 }
 }
}
