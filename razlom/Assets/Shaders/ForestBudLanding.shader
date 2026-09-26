Shader "Razlom/Forest Bud Landing"
{
 Properties { _Progress("Заполнение",Range(0,1))=0
 _Opacity("Видимость",Range(0,1))=1
 _Radius("Радиус опасности, м",Float)=1
  }
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
 float _NeighborCount;
 float4 _NearbyDisks[8];
 CBUFFER_END
 V Vert(A a){V o;o.world=TransformObjectToWorld(a.vertex.xyz);o.position=TransformWorldToHClip(o.world);o.uv=a.uv;return o;}
 half4 Frag(V i):SV_Target { float r=length(i.uv*2-1);
 float exposed=1, coverage=1;
 for(int n=0;n<(int)_NeighborCount;n++) {
 float d=length(i.world.xz-_NearbyDisks[n].xy)-_NearbyDisks[n].z;
 exposed*=smoothstep(-.03,.03,d);
 if(_NearbyDisks[n].w>.5)coverage*=smoothstep(-.01,.01,d); }
 return GroundTelegraph(i.world.xz,(1-r)*_Radius,r,_Progress,_Opacity*coverage,exposed,0); }
 ENDHLSL
 }
 }
}
