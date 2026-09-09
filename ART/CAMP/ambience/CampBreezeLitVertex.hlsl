#include "CampBreeze.hlsl"

Varyings CampBreezeVertex(CampBreezeAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    Attributes original = (Attributes)0;
    UNITY_TRANSFER_INSTANCE_ID(input, original);
    original.positionOS = float4(CampBreezePosition(input.positionOS.xyz, input.bend, _CampBreeze.w), 1);
    original.normalOS = input.normalOS;
    original.tangentOS = input.tangentOS;
    original.texcoord = input.uv;
    original.staticLightmapUV = input.staticLightmapUV;
    original.dynamicLightmapUV = input.dynamicLightmapUV;
    return CAMP_BREEZE_LIT_VERTEX(original);
}
