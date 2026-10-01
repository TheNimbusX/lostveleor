Shader "Razlom/Pelag Basic Cone Candidate"
{
    Properties
    {
        _Core("Silver edge",Color)=(2.4,2.5,2.7,1)
        _Body("Silver body",Color)=(.95,1.04,1.18,1)
        _Opacity("Opacity",Range(0,1))=1
        _Head("Angular sweep",Range(0,1))=1
        _Expand("Radial spread",Range(0,1))=1
        _Dissolve("Tail dissolution",Range(0,1))=0
        _Flow("Motion clock",Float)=0
        _Layer("0 cone / 1 edge / 2 echo",Float)=0
        _Grain("Pack grain",2D)="white"{}
        _Mask("Pack edge mask",2D)="white"{}
    }
    SubShader
    {
        Tags {"RenderType"="Transparent" "Queue"="Transparent+40" "RenderPipeline"="UniversalPipeline"}
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            CBUFFER_START(UnityPerMaterial)
            half4 _Core,_Body;
            float _Opacity,_Head,_Expand,_Dissolve,_Flow,_Layer;
            CBUFFER_END
            TEXTURE2D(_Grain);SAMPLER(sampler_Grain);
            TEXTURE2D(_Mask);SAMPLER(sampler_Mask);
            V Vert(A a) {V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.uv=a.uv;o.color=a.color;return o;}
            half4 Frag(V i):SV_Target
            {
                float u=i.uv.x,r=i.uv.y;
                float aa=max(fwidth(u),.001);
                float swept=1-smoothstep(_Head-aa,_Head+aa,u);
                float expanded=1-smoothstep(_Expand-.012,_Expand+.012,r);
                float sides=smoothstep(0,.022,u)*(1-smoothstep(.978,1,u));
                float grain=SAMPLE_TEXTURE2D(_Grain,sampler_Grain,float2(u*6,r*.38-_Flow)).r;
                float brushed=SAMPLE_TEXTURE2D(_Grain,sampler_Grain,float2(u*3.5,r*.12-_Flow*.7)).r;
                float dissolve=smoothstep(_Dissolve-.06,_Dissolve+.06,grain*.30+r*.70);
                half3 color=_Body.rgb;
                float alpha;
                if(_Layer<.5)
                {
                    // An actual filled fan: no inner cut-out and no narrow annular silhouette.
                    float root=smoothstep(0,.065,r);
                    float mass=.37+.22*sin(r*3.14159);
                    float forwardRidge=smoothstep(.73,.81,r)*(1-smoothstep(.85,.90,r));
                    color=lerp(_Body.rgb,_Core.rgb,forwardRidge*.16+brushed*.015);
                    alpha=root*mass*lerp(.94,1,grain);
                }
                else if(_Layer<1.5)
                {
                    float mask=SAMPLE_TEXTURE2D(_Mask,sampler_Mask,i.uv).r;
                    float profile=pow(saturate(sin(u*3.14159)),.32);
                    float width=smoothstep(1-profile-.025,1-profile+.025,r);
                    float rim=pow(saturate(r),.65);
                    color=lerp(_Body.rgb,_Core.rgb,smoothstep(.3,.8,r));
                    alpha=width*rim*smoothstep(.04,.35,mask);
                    expanded=1; // The edge mesh already occupies the far tip of the cone.
                }
                else
                {
                    color=_Body.rgb;
                    alpha=smoothstep(.05,.30,r)*(1-smoothstep(.72,1,r))*.22;
                }
                alpha*=sides*swept*expanded*dissolve*_Opacity*i.color.a;
                return half4(color*alpha*i.color.rgb,alpha);
            }
            ENDHLSL
        }
    }
}
