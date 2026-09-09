#ifndef CAMP_BREEZE_INCLUDED
#define CAMP_BREEZE_INCLUDED

// xy — единое направление, z — сила, w — время игры (пауза останавливает ветер).
float4 _CampBreeze;
float _CampBreezePreviousTime;

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

float3 CampBreezePosition(float3 positionOS, float4 bend, float time)
{
    UNITY_BRANCH
    if (_CampBreeze.z <= 0 || bend.w == 0) return positionOS;
    float3 root = TransformObjectToWorld(bend.xyz);
    float3 p = TransformObjectToWorld(positionOS) - root;
    float phase = frac(sin(dot(root.xz, float2(12.9898, 78.233))) * 43758.5453) * TWO_PI;
    float slow = sin(time * 1.12 + phase);
    float detail = sin(time * 2.31 + phase * 1.73);
    float gust = .72 + .28 * sin(time * .37 + phase * .31);
    float angle = bend.w * _CampBreeze.z * (slow * .78 + detail * .22) * gust;
    float s, c;
    sincos(angle, s, c);
    float3 axis = float3(_CampBreeze.y, 0, -_CampBreeze.x);
    // Поворот вокруг корня сохраняет форму растения; камни и грунт не получают этот шейдер.
    p = p * c + cross(axis, p) * s + axis * dot(axis, p) * (1 - c);
    return TransformWorldToObject(root + p);
}

#endif
