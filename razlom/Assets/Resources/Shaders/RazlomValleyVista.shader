Shader "Razlom/Valley Vista"
{
    // Долина под обрывом арены (LayoutView.Vista). Одна плоскость: лес «издалека» — два яруса круглых
    // крон вчетверо мельче настоящих, рощи елей, луга с редкими кустами, река с озером, каменные
    // осыпи, тень обрыва у его подножия, гряды холмов и дымка, густеющая к горизонту.
    // Камера ортографическая: даль передаётся масштабом рисунка, тенью и дымкой, а не перспективой.
    Properties
    {
        _Edge ("Edge: normal xz, line, hidden strip", Vector) = (0,1,0,10)
        _Along ("Along: tangent xz, phase, lake", Vector) = (1,0,0,0)
        _Haze ("Haze", Color) = (.64,.73,.70,1)
        _Reach ("Haze reach, m", Float) = 48
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+5" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Edge, _Along;
                half4 _Haze;
                float _Reach;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; };
            struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; half fog:TEXCOORD1; };
            V Vert(A v)
            {
                V o; o.world=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.world);
                o.fog=ComputeFogFactor(o.positionCS.z); return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float2 Hash2(float2 p) { return float2(Hash(p),Hash(p+float2(37.3,91.7))); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            // Ярус крон: круглые шапки в ячейках Вороного. keep — доля ячеек с деревом, dark — доля ельника.
            // rgb — цвет шапки с бликом к солнцу, a — покрытие.
            half4 Crowns(float2 p, float scale, float keep, float dark, float2 sun)
            {
                float2 q=p*scale+float2(Noise(p*.9+scale),Noise(p*.9+7.3+scale))*.35, cell=floor(q), f=frac(q);
                float best=8, id=0; float2 toCenter=0;
                for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++)
                {
                    float2 o=float2(x,y), r=o+Hash2(cell+o+scale)-f;
                    float d=dot(r,r);
                    if(d<best){ best=d; toCenter=r; id=Hash(cell+o+17+scale); }
                }
                best=sqrt(best);
                float planted=step(frac(id*13.7),keep);
                float spruce=step(frac(id*5.3),dark);
                float radius=lerp(.44,.66,frac(id*7.31))*lerp(1,.78,spruce);
                float cap=smoothstep(radius,radius-.14,best)*planted;
                float glow=saturate(1-length(-toCenter-sun*radius*.38)/(radius*.95));
                half3 crown=lerp(half3(.075,.18,.06),half3(.18,.32,.085),id);
                crown=lerp(crown,half3(.22,.33,.09),step(.93,id));
                crown=lerp(crown,lerp(half3(.04,.12,.09),half3(.07,.17,.12),id),spruce);
                // Мягкий объём без блика-«капли»: светлее к солнцу, комковатая листва.
                crown*=(.62+.6*glow)*(.74+.5*Noise(p*scale*4.3+id*9));
                return half4(crown,cap);
            }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.world.xz;
                float t=_Time.y;
                float across=dot(p,_Edge.xy)-_Edge.z, along=dot(p,_Along.xy);
                float seen=across-_Edge.w;
                Light light=GetMainLight();
                float2 sun=normalize(light.direction.xz+float2(1e-4,0));

                // Рощи: где густо, где редколесье, где ельник.
                float grove=Noise(p*.05+float2(3.1,8.4))*.75+Noise(p*.17)*.25;
                float open=smoothstep(.62,.72,grove);
                float fir=smoothstep(.55,.7,Noise(p*.06+float2(71.3,12.9)));

                // Земля: тёмная подстилка под лесом, трава с пятнами на лугах.
                half3 turf=half3(.24,.35,.12)*(.82+.3*Noise(p*.9))*(.92+.16*Noise(p*4.1));
                half3 color=lerp(half3(.035,.085,.04),turf,open);

                // Нижний ярус: на лугах — редкие кусты и одиночные деревья, в лесу — сплошной полог.
                half4 low=Crowns(p,2.3,lerp(1,.16,open),fir*.8,sun);
                float lowShade=Crowns(p+sun*.22,2.3,lerp(1,.16,open),fir*.8,sun).a;
                color*=1-.4*lowShade*open;
                color=lerp(color,low.rgb,low.a);
                // Верхний ярус: редкие великаны выше полога, с тенью на соседей.
                half4 tall=Crowns(p,1.05,.34*(1-open*.75),fir*.6,sun);
                float tallShade=Crowns(p+sun*.42,1.05,.34*(1-open*.75),fir*.6,sun).a;
                color*=1-.42*tallShade*(1-tall.a);
                color=lerp(color,tall.rgb*1.12,tall.a);

                // Каменные осыпи: редкие тёмные массивы со светом по склонам.
                float stony=smoothstep(.76,.82,Noise(p*.07+float2(41.3,17.9))*.8+Noise(p*.31)*.2);
                if(stony>0)
                {
                    float2 rp=p*.55;
                    float rh=Noise(rp)+.5*Noise(rp*2.7);
                    float2 rg=float2(Noise(rp+float2(.12,0))+.5*Noise((rp+float2(.12,0))*2.7)-rh,
                        Noise(rp+float2(0,.12))+.5*Noise((rp+float2(0,.12))*2.7)-rh);
                    half3 rock=lerp(half3(.10,.10,.09),half3(.17,.16,.14),saturate(rh*.6));
                    rock*=(1+clamp(dot(rg,sun)*9,-.45,.6))*(.85+.3*Noise(p*3.7));
                    color=lerp(color,rock,stony);
                }

                // Река петляет вдоль обрыва и впадает в озеро: тёмная глубина, светлая отмель, кусты по берегу.
                float bed=_Edge.w+5+2.6*sin(along*.05+_Along.z)+1.2*sin(along*.13+_Along.z*1.7);
                float width=1.3+.45*sin(along*.07+1.3);
                float away=abs(across-bed)-width;
                float2 lake=float2((along-_Along.w)/9.5,(across-_Edge.w-6)/4.2);
                float bearing=atan2(lake.y,lake.x);
                float shore=(length(lake)-1-.16*sin(bearing*3+_Along.z)-.08*sin(bearing*5))*4.2;
                float wet=min(away,shore);
                color=lerp(color,half3(.27,.30,.16),smoothstep(.9,0,wet)*.75);
                half3 water=lerp(half3(.20,.37,.40),half3(.08,.21,.29),smoothstep(-.1,-1.4,wet));
                water+=step(.9,Noise(p*5+float2(t*.6,-t*.3)))*.22*smoothstep(-.2,-.8,wet);
                color=lerp(color,water,smoothstep(0,-.35,wet));

                // Рельеф: пологие увалы светлее к солнцу и темнее в тени.
                float e=1.5;
                float2 slope=float2(Noise((p+float2(e,0))*.045)-Noise((p-float2(e,0))*.045),Noise((p+float2(0,e))*.045)-Noise((p-float2(0,e))*.045));
                color*=1+clamp(dot(slope,sun)*9,-.22,.22)*step(.4,wet);
                half3 woods=color;

                // Тень обрыва лежит на его подножии: край плато отделён от долины глубиной, а не линией.
                color*=lerp(.3,1,smoothstep(-1,7,seen+1.5*Noise(float2(along*.3,3.3))));
                // Тени облаков плывут и по долине.
                color*=lerp(.78,1.05,smoothstep(.35,.65,Noise(p*.03+float2(t*.012,t*.009))));
                half3 tone=lerp(half3(1,1,1),light.color,.3);
                color*=tone;

                // Дымка: у подножия лёгкая, к горизонту долина растворяется; клочья тумана медленно ползут.
                float haze=.1+.6*smoothstep(0,1,seen/_Reach);
                haze+=(Noise(p*.06+float2(t*.03,0))-.5)*.2+(Noise(p*.21-float2(0,t*.02))-.5)*.08;
                color=lerp(color,_Haze.rgb,saturate(haze));

                // Дальние холмы: три гряды, покрытые тем же лесом; ближний склон под гребнем в тени,
                // дальше гряда тонет в тумане, каждая следующая светлее.
                for(int k=0;k<3;k++)
                {
                    float crest=_Edge.w+12.5+k*5+3.2*Noise(float2(along*.06+k*13.7,k*3.1))+1.6*Noise(float2(along*.21+k*5.3,7.7));
                    float into=across-crest;
                    half3 ridge=lerp(woods*tone*lerp(.5,1,smoothstep(0,1.8,into)),_Haze.rgb,.3+.18*k);
                    ridge=lerp(ridge,_Haze.rgb,smoothstep(1.5,5.5,into)*.8);
                    color=lerp(color,ridge,smoothstep(0,.35,into));
                }
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
