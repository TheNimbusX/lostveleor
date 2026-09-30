#ifndef CAMP_DEPTH_GROUND_INCLUDED
#define CAMP_DEPTH_GROUND_INCLUDED
float _CampDepthOn;
float4 _CampDepthCentre, _CampDepthControls, _CampDepthEarthStrength;
float4 _CampDepthDryTint, _CampDepthWetTint, _CampDepthAshTint, _CampDepthWarmTint;
float4 _CampDepthDryZones[5], _CampDepthAshZones[2];
float4x4 _CampDepthRiverToLocal;
TEXTURE2D(_CampDepthRiverBoundary); SAMPLER(sampler_CampDepthRiverBoundary);
float4 _CampDepthRiverRange, _CampDepthRiverShape;
float CampDepthHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
float CampDepthNoise(float2 p)
{
    float2 i=floor(p),f=frac(p); f=f*f*(3-2*f);
    return lerp(lerp(CampDepthHash(i),CampDepthHash(i+float2(1,0)),f.x),lerp(CampDepthHash(i+float2(0,1)),CampDepthHash(i+1),f.x),f.y);
}
float CampDepthZone(float2 p,float4 zone)
{ return (1-smoothstep(zone.z*.24,max(zone.z,.01),distance(p,zone.xy)))*zone.w; }
void CampDepthEarth(float3 position,half3 dirt,inout half3 albedo,inout half occlusion)
{
    if (_CampDepthOn < .5) return;
    float2 p=position.xz; float macro=CampDepthNoise(p*.18+43);
    float dry=0; for(int i=0;i<5;i++) dry=max(dry,CampDepthZone(p,_CampDepthDryZones[i]));
    dry*=lerp(.76,1,macro);
    half3 dryEarth=lerp(dirt,albedo,.24)*_CampDepthDryTint.rgb;
    albedo=lerp(albedo,dryEarth,dry*_CampDepthEarthStrength.x);
    float wet=0;
    if(_CampDepthRiverRange.w>.5)
    {
        float3 river=mul(_CampDepthRiverToLocal,float4(position,1)).xyz;
        float u=saturate((river.x-_CampDepthRiverRange.x)/max(.001,_CampDepthRiverRange.y));
        u=lerp(_CampDepthRiverRange.z*.5,1-_CampDepthRiverRange.z*.5,u);
        float edge=SAMPLE_TEXTURE2D(_CampDepthRiverBoundary,sampler_CampDepthRiverBoundary,float2(u,.5)).r;
        float distanceToBank=min(abs(river.z-edge),abs(river.z-(edge-_CampDepthRiverShape.x)))*_CampDepthRiverShape.y;
        wet=(1-smoothstep(.2,_CampDepthEarthStrength.w,distanceToBank))*lerp(.70,1,macro);
    }
    albedo*=lerp(half3(1,1,1),_CampDepthWetTint.rgb,wet*_CampDepthEarthStrength.y*(1-dry*.3));
    float ash=max(CampDepthZone(p,_CampDepthAshZones[0]),CampDepthZone(p,_CampDepthAshZones[1]));
    albedo=lerp(albedo,lerp(albedo,dirt*.83,.48)*_CampDepthAshTint.rgb,ash*_CampDepthEarthStrength.z*lerp(.8,1,macro));
    float warm=1-smoothstep(_CampDepthCentre.z,_CampDepthCentre.w,distance(p,_CampDepthCentre.xy));
    albedo*=lerp(half3(1,1,1),_CampDepthWarmTint.rgb,warm);
    // Усиливается существующая карта контакта, а не рисуется новый тёмный круг под героем.
    occlusion=max(.54,occlusion-(1-occlusion)*_CampDepthControls.z);
}
#endif
