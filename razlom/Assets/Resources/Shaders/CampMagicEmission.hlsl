#ifndef CAMP_MAGIC_EMISSION_INCLUDED
#define CAMP_MAGIC_EMISSION_INCLUDED
float _MagicKind, _MagicStrength, _MagicPhase, _CampMagicClock;

half3 CampMagicEmission(half3 albedo, float3 p, float3 normal, float3 view)
{
    // Отделяем нарисованную магию по её цвету и положению в модели: камень остаётся матовым.
    float high=max(albedo.r,max(albedo.g,albedo.b));
    float low=min(albedo.r,min(albedo.g,albedo.b));
    float saturation=(high-low)/max(high,.001);
    float gold=smoothstep(.64,.91,saturation)*smoothstep(.18,.55,albedo.r)
        *smoothstep(.22,.5,albedo.g/max(albedo.r,.001))*(1-smoothstep(.12,.35,albedo.b/max(high,.001)));
    float cyan=smoothstep(.08,.3,min(albedo.g,albedo.b)-albedo.r)*smoothstep(.25,.65,high);
    float green=smoothstep(.11,.4,albedo.g-max(albedo.r*.8,albedo.b*1.4))*smoothstep(.4,.8,saturation);
    float t=_CampMagicClock;
    float breath=.82+.18*sin(t*.95+_MagicPhase);
    float flow=pow(.5+.5*sin(p.y*16-t*1.9+_MagicPhase),5);
    half3 emission=0;
    if (_MagicKind<.5)
        emission=half3(1.55,.43,.045)*gold*smoothstep(.30,.62,albedo.g)*(.65+.35*breath);
    else if (_MagicKind<1.5)
    {
        float crystalArea=smoothstep(.23,.3,p.y)*(1-smoothstep(.15,.24,abs(p.x)));
        float rim=pow(1-saturate(dot(normalize(normal),normalize(view))),2);
        emission=half3(.035,.49,1.3)*cyan*crystalArea*(.48+.32*breath+.3*flow+.32*rim);
    }
    else if (_MagicKind<2.5)
    {
        float liquid=smoothstep(.27,.35,p.y)*(1-smoothstep(.59,.7,p.y));
        emission=half3(.35,1.3,.035)*green*liquid*(.63+.3*breath+.18*flow);
    }
    else if (_MagicKind<3.5)
        emission=half3(1.25,.52,.055)*gold*smoothstep(.49,.80,albedo.g)*(.55+.45*breath+.25*flow);
    else if (_MagicKind<4.5)
    {
        float red=smoothstep(.18,.5,albedo.r-max(albedo.g,albedo.b))*smoothstep(.7,.92,saturation);
        float wave=.55+.45*pow(.5+.5*sin(length(p.xz)*17+t*2.2),3);
        emission=(half3(2,.38,.035)*red+half3(1.6,.85,.08)*gold+half3(.04,.85,1.2)*cyan+half3(.3,1.1,.025)*green)*wave;
    }
    else
    {
        float rim=pow(1-saturate(dot(normalize(normal),normalize(view))),2.3);
        emission=half3(.09,.9,1.1)*cyan*(.6+.32*breath+.32*flow+.35*rim);
    }
    return emission*_MagicStrength;
}
#endif
