#ifndef RAZLOM_GROUND_TELEGRAPH_STYLE
#define RAZLOM_GROUND_TELEGRAPH_STYLE
// Approved 2026-09-25. Shared enemy warning style. Distances are metres.
half4 GroundTelegraph(float2 worldXZ, float distanceInside, float fillCoordinate,
    float progress, float opacity, float exposedBoundary, float directionMark)
{
    float aa=max(fwidth(distanceInside),.002);
    float inside=smoothstep(-aa,aa,distanceInside);
    float rim=(1-smoothstep(.040-aa,.040+aa,distanceInside))*inside;
    float ink=(1-smoothstep(.062-aa,.062+aa,distanceInside))*inside;
    float highlight=smoothstep(.031-aa,.035+aa,distanceInside)*(1-smoothstep(.041-aa,.046+aa,distanceInside))*inside;
    float p=saturate(progress);
    float fill=1-smoothstep(p-.018,p+.018,fillCoordinate);
    float front=(1-smoothstep(.006,.025,abs(fillCoordinate-p)))*step(.015,p)*(1-step(.995,p));
    float urgency=smoothstep(.80,1,p);
    // Quiet, stationary pigment variation; never distorts the danger boundary.
    float pigment=.94+.06*sin(worldXZ.x*6.1+sin(worldXZ.y*4.7))*sin(worldXZ.y*5.4+worldXZ.x*1.7);
    float3 color=lerp(float3(.46,.012,.026),float3(.72,.028,.027),fill)*pigment;
    float alpha=(.12+.20*fill+.035*urgency)*inside;
    color=lerp(color,float3(.08,.008,.015),ink*exposedBoundary);
    alpha=max(alpha,ink*.78*exposedBoundary);
    color=lerp(color,float3(1,.105,.065),rim*exposedBoundary);
    alpha=max(alpha,rim*.94*exposedBoundary);
    color=lerp(color,float3(1,.37,.22),highlight*exposedBoundary*(.58+.42*urgency));
    color=lerp(color,float3(1,.22,.13),front*.45*(1-ink));
    alpha=max(alpha,front*.40*inside);
    color=lerp(color,float3(1,.25,.16),directionMark*.65);
    alpha=max(alpha,directionMark*.55*inside);
    return half4(color,saturate(alpha)*saturate(opacity));
}
#endif
