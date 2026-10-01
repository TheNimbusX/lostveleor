Shader "Razlom/Candidate/Pelag Reference Silver R07" {
 Properties {_BaseMap("Painted pack stroke",2D)="white"{} _BaseColor("Silver",Color)=(1,1,1,1) _Animated("Stroke",Float)=0 _Reverse("Reverse",Float)=0 _Downward("Finisher",Float)=0 _Phase("Age",Float)=0}
 SubShader {Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
 Pass {Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST;half4 _BaseColor;float _Animated,_Reverse,_Downward,_Phase;
 CBUFFER_END
 struct A {float4 p:POSITION;float2 uv:TEXCOORD0;float2 param:TEXCOORD1;half4 color:COLOR;};
 struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;float3 stroke:TEXCOORD1;};
 V vert(A a) {V v;float3 p=a.p.xyz;float age=saturate(_Phase);float drive=smoothstep(0,.34,age);
 if(_Animated>.5){p.xz*=.92+.08*drive;p.y+=sin((a.param.x-drive*.6)*3.14159)*.055*(1-age);}
 v.p=TransformObjectToHClip(p);v.uv=a.uv*_BaseMap_ST.xy+_BaseMap_ST.zw;v.color=a.color;v.stroke=float3(a.param,age);return v;}
 half4 frag(V v):SV_Target {
 half4 t=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,v.uv);
 float grain=max(t.r,max(t.g,t.b));float vertexIntensity=max(v.color.r,max(v.color.g,v.color.b));
 float alpha=t.a*v.color.a*_BaseColor.a*pow(saturate(grain),.85);
 if(_Animated>.5){float coord=lerp(v.stroke.x,1-v.stroke.x,_Reverse);
 // Every surface follows the progressing blade. The finisher retains the
 // same frontal sector; no radial pop or ring around the hero.
 float head=1.12*smoothstep(0,.40,v.stroke.z);
 float tail=1.12*smoothstep(.46,1,v.stroke.z);
 alpha*=(1-smoothstep(head-.035,head+.025,coord))*smoothstep(tail-.10,tail+.04,coord);
 alpha*=.60+.40*pow(saturate(v.stroke.y),.85);
 }
 alpha=saturate(alpha);if(!(alpha>.002))discard;
 return half4(grain*vertexIntensity*_BaseColor.rgb,alpha);
 }
 ENDHLSL
 }} }
