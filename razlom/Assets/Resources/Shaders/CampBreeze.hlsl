#ifndef CAMP_BREEZE_INCLUDED
#define CAMP_BREEZE_INCLUDED

// xy — единое направление, z — сила, w — время игры (пауза останавливает ветер).
float4 _CampBreeze;
float _CampBreezePreviousTime;
float _CampFlagStrength;
TEXTURE2D(_CampPaintedPaths); SAMPLER(sampler_CampPaintedPaths);
float4 _CampPaintedPathBounds;
float _CampPathFoliage;
float4x4 _CampRiverWorldToLocal;
TEXTURE2D(_CampRiverBoundary); SAMPLER(sampler_CampRiverBoundary);
float4 _CampRiverBoundaryRange;
float4 _CampRiverMask;
// x — от кромки травы ближнего берега до воды, y — ширина воды (CampRiver.RiverBand).
float4 _CampRiverBand;

struct CampBreezeAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 staticLightmapUV : TEXCOORD1;
    float2 dynamicLightmapUV : TEXCOORD2;
    // xyz — закреплённое основание растения в меше, w — угол изгиба этой вершины.
    float4 bend : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float CampBreezeHash(float2 p)
{
    return frac(sin(dot(p, float2(41.37, 289.13))) * 43758.5453);
}

float3 CampBreezePosition(float3 positionOS, float4 bend, float time)
{
    float3 root = TransformObjectToWorld(bend.xyz);
    if (_CampPathFoliage > .5 && _CampRiverMask.z > .5)
    {
        float3 river = mul(_CampRiverWorldToLocal, float4(root,1)).xyz;
        float u=saturate((river.x-_CampRiverBoundaryRange.x)/max(.001,_CampRiverBoundaryRange.y));
        u=lerp(_CampRiverBoundaryRange.z*.5,1-_CampRiverBoundaryRange.z*.5,u);
        float edge=SAMPLE_TEXTURE2D_LOD(_CampRiverBoundary,sampler_CampRiverBoundary,float2(u,.5),0).r;
        // Прячется только вода (аудит 23 сентября); берега и дальний берег зарастают.
        // Без полосы (старый CampRiver) — как раньше: всё южнее кромки.
        float water = edge - _CampRiverBand.x;
        if(_CampRiverBand.y > 0 ? (river.z < water && river.z > water - _CampRiverBand.y) : river.z < edge)return bend.xyz;
    }
    // Все вершины одного растения сходятся к его корню: одинаково в цвете,
    // тенях, depth и motion vectors. Исходный меш остаётся целым для ластика/Undo.
    float shrink = 1;
    if (_CampPathFoliage > .5 && _CampPaintedPathBounds.z > 0 && _CampPaintedPathBounds.w > 0)
    {
        float2 uv = (root.xz - _CampPaintedPathBounds.xy) / _CampPaintedPathBounds.zw;
        if (all(uv >= 0) && all(uv <= 1))
        {
            // Край дорожки без ступеньки (аудит 23 сентября): порог у каждого растения свой,
            // а в полосе ~0,4 м вокруг дорожки трава редеет и мельчает.
            float2 tap = .4 / _CampPaintedPathBounds.zw;
            float centre = SAMPLE_TEXTURE2D_LOD(_CampPaintedPaths, sampler_CampPaintedPaths, uv, 0).r;
            float soft = (SAMPLE_TEXTURE2D_LOD(_CampPaintedPaths, sampler_CampPaintedPaths, uv + float2(tap.x, 0), 0).r
                + SAMPLE_TEXTURE2D_LOD(_CampPaintedPaths, sampler_CampPaintedPaths, uv - float2(tap.x, 0), 0).r
                + SAMPLE_TEXTURE2D_LOD(_CampPaintedPaths, sampler_CampPaintedPaths, uv + float2(0, tap.y), 0).r
                + SAMPLE_TEXTURE2D_LOD(_CampPaintedPaths, sampler_CampPaintedPaths, uv - float2(0, tap.y), 0).r) * .25;
            if (centre > lerp(.46, .84, CampBreezeHash(root.xz))) return bend.xyz;
            if (soft > lerp(.3, 1.05, CampBreezeHash(root.zx + 17.1))) return bend.xyz;
            shrink = lerp(1, .6, saturate(max(soft, centre) * 1.6));
        }
    }
    UNITY_BRANCH
    if (_CampBreeze.z <= 0 || bend.w == 0)
    {
        if (shrink > .999) return positionOS;
        return TransformWorldToObject(root + (TransformObjectToWorld(positionOS) - root) * shrink);
    }
    float3 p = (TransformObjectToWorld(positionOS) - root) * shrink;
    float phase = frac(sin(dot(root.xz, float2(12.9898, 78.233))) * 43758.5453) * TWO_PI;
    float slow = sin(time * 1.12 + phase);
    float detail = sin(time * 2.31 + phase * 1.73);
    float gust = .72 + .28 * sin(time * .37 + phase * .31);
    // Отрицательная маска — ткань, положительная — растительность.
    float amplitude = bend.w < 0 ? -bend.w * _CampFlagStrength : bend.w;
    float angle = amplitude * _CampBreeze.z * (slow * .78 + detail * .22) * gust;
    float s, c;
    sincos(angle, s, c);
    float3 axis = float3(_CampBreeze.y, 0, -_CampBreeze.x);
    // Поворот вокруг корня сохраняет форму растения; камни и грунт не получают этот шейдер.
    p = p * c + cross(axis, p) * s + axis * dot(axis, p) * (1 - c);
    return TransformWorldToObject(root + p);
}

#endif
