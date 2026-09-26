Shader "Razlom/Wendigo Warning"
{
 Properties { _Progress("Заполнение",Range(0,1))=0
 _Opacity("Видимость",Range(0,1))=1
 _Radius("Радиус опасности, м",Float)=1
 _IsSector("Сектор",Float)=1
 _Impact("Попадание",Float)=0
 _Ring("Доля радиуса",Range(.3,1))=1 }
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+12" "RenderType"="Transparent" }
 Pass {
 Blend SrcAlpha OneMinusSrcAlpha
 ZWrite Off Cull Off Offset -1,-1
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "GroundTelegraphStyle.hlsl"
 struct A {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
 struct V {float4 position:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;};
 CBUFFER_START(UnityPerMaterial)
 float _Progress,_Opacity,_Radius;
 float _IsSector,_Impact,_Ring;
 CBUFFER_END
 V Vert(A a){V o;o.world=TransformObjectToWorld(a.vertex.xyz);o.position=TransformWorldToHClip(o.world);o.uv=a.uv;return o;}
 half4 Frag(V i):SV_Target { float r=i.uv.y/max(.01,_Ring);
 float edge=(1-r)*_Radius;
 if(_IsSector>.5)edge=min(edge,sin(min(i.uv.x,1-i.uv.x)*2.44346095)*r*_Radius);
 return GroundTelegraph(i.world.xz,edge,r,_Progress,_Opacity,1,0); }
 ENDHLSL
 }
 }
}
