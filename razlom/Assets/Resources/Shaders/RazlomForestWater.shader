Shader "Razlom/Forest Water"
{
    Properties
    {
        _DeepColor ("Deep water", Color) = (.10,.24,.26,1)
        _ShallowColor ("Shallow water", Color) = (.25,.36,.27,1)
        _BedColor ("Shore bed", Color) = (.33,.31,.21,1)
        _FoamColor ("Shore line", Color) = (.62,.66,.52,1)
        _PadColor ("Lily pads", Color) = (.24,.36,.11,1)
        _PadChance ("Lily pad chance", Range(0,1)) = .16
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor, _ShallowColor, _BedColor, _FoamColor, _PadColor;
                half _PadChance;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 pond:TEXCOORD1; };
            struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float2 uv:TEXCOORD1; half fog:TEXCOORD2; float4 pond:TEXCOORD3; };
            V Vert(A v)
            {
                V o; o.world=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.world); o.uv=v.uv; o.pond=v.pond;
                o.fog=ComputeFogFactor(o.positionCS.z); return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            // Кувшинки (8 октября): редкие листья в мелкой части пруда, у каждого свой поворот и вырез.
            // Возвращает покрытие листа и светлый ободок; цветок — в z.
            float3 Pads(float2 p, float4 pond)
            {
                float2 cell=floor(p/1.15), local=frac(p/1.15)-.5;
                float h=Hash(cell);
                float shore=length(((cell+.5)*1.15-pond.xy)/max(.5,pond.zw));
                if(h>_PadChance || shore<.5 || shore>.82) return 0;
                float2 c=local-(float2(Hash(cell+7),Hash(cell+13))-.5)*.35;
                float r=lerp(.24,.4,Hash(cell+3)), d=length(c);
                float turn=Hash(cell+5)*6.2832, a=atan2(c.y,c.x)-turn;
                float notch=step(cos(a),.94);
                float pad=smoothstep(r,r-.025,d)*notch;
                float rim=pad*smoothstep(r-.07,r-.01,d);
                float flower=h<_PadChance*.3 ? smoothstep(.09,.06,length(c+float2(cos(turn+2.4),sin(turn+2.4))*r*.35)) : 0;
                return float3(pad,rim,flower);
            }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.world.xz;
                float t=_Time.y;
                float ripple=sin(p.x*3.1+p.y*1.7+sin(p.y*.7)+t*.65)*.6
                    + sin(p.y*2.4-p.x*.9-t*.43)*.4;
                float shore=length(i.uv);
                float n=Noise(p*.45+t*.03), fine=Noise(p*1.7-t*.05);
                // Глубина плавно: мелководье просвечивает бурым дном, середина — тёмная бирюза.
                float shallow=smoothstep(.35,1,shore+(n-.5)*.22);
                half3 color=lerp(_DeepColor.rgb,_ShallowColor.rgb,shallow);
                color=lerp(color,_BedColor.rgb,smoothstep(.78,1,shore+(fine-.5)*.08)*.6);
                // Тёмные пятна глубины и светлые разводы неба — вода не читается плоской заливкой.
                color*=lerp(.86,1.08,smoothstep(.3,.75,Noise(p*.22-t*.015)));
                half3 normal=normalize(half3(ripple*.025,1,cos(p.x*2.1-p.y*2+t*.5)*.02));
                Light light=GetMainLight();
                half3 lit=max(SampleSH(normal),half3(.35,.38,.35))+light.color*saturate(dot(normal,light.direction))*.7;
                color*=lit;
                half3 view=GetWorldSpaceNormalizeViewDir(i.world);
                float glint=pow(saturate(dot(normal,normalize(light.direction+view))),64);
                // Солнечные искры — редкие, мерцают по шуму, только на открытой воде.
                float sparkle=step(.93,Noise(p*5.3+float2(t*.7,-t*.4)))*step(.6,Noise(p*1.3-t*.2))*(1-shallow*.6);
                color+=light.color*(glint*.05+sparkle*.22)+ripple*.003;
                // Светлая кромка у берега дышит и рвётся шумом.
                float shoreLine=smoothstep(.9,.975,shore)*smoothstep(1.02,.985,shore);
                shoreLine*=smoothstep(.35,.7,Noise(p*1.4+float2(t*.12,t*.08)))*(.7+.3*sin(t*1.3+p.x*.8+p.y*.6));
                color=lerp(color,_FoamColor.rgb*lit,shoreLine*.3);
                float3 pad=Pads(p,i.pond);
                half3 leaf=_PadColor.rgb*(1+pad.y*.45)*(max(SampleSH(half3(0,1,0)),half3(.35,.38,.35))+light.color*saturate(light.direction.y)*.8);
                color=lerp(color,leaf,pad.x);
                color=lerp(color,half3(1,.78,.86)*lit*1.15,pad.z*pad.x);
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
