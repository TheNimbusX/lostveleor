// Крушение «холодное железо» (06.10, V6): камни круга и полосы — гранёные плиты и блоки, комья земли,
// железные осколки, крупные звенья цепи в полосе, летящие куски (меш-частицы). Рисованный свет двумя
// тонами (как у тел), тёмный контур: толстая «скорлупа» (_HullWidth, м, проход SRPDefaultUnlit, по
// сглаженной нормали из uv3) + тонкий контур силуэта UnitOutlineMask (UnitOutlineFeature).
//  • камень (_Core 0) — серо-бурый: фактура камня арены × _BaseColor × _Tint (на кусок), приглушена
//    _Saturation; _Earth уводит в тёмный ком земли; подошва светится из трещины только в миг выхода;
//  • звено (_Core 1, V6) — тёмное железо (#2A2F36) со стальными бликами и толстым контуром; голубой
//    #4FA8FF → #9CD8FF и белые блики идут только изнутри: внутренняя сторона прута (к дыре звена),
//    щель у земли (_GroundY/_GlowHeight) и жилы-трещины в железе (_BaseMap — маска трещин Hovl).
// Частицы (_VertexColor 1) берут цвет частицы (ком земли, камень, железо).
// Гаснет вид оседанием в землю и масштабом (непрозрачный, без альфы).
Shader "Razlom/Wreck Iron"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Tint ("Piece Tint", Color) = (1,1,1,1)
        _ShadeColor ("Shade Color", Color) = (0.40,0.36,0.34,1)
        _LightThreshold ("Light Threshold", Range(0,1)) = 0.5
        _LightFeather ("Light Feather", Range(0.001,0.3)) = 0.07
        _AmbientWeight ("Ambient Weight", Range(0,1.5)) = 0.55
        _EarthColor ("Earth Clod Color", Color) = (0.30,0.20,0.13,1)
        _Earth ("Earth Clod", Range(0,1)) = 0
        [HDR] _GlowColor ("Glow Color", Color) = (0.31,0.66,1.0,1)
        [HDR] _HotColor ("Hot Color (pale)", Color) = (0.61,0.85,1.0,1)
        [HDR] _GlintColor ("Glint Color", Color) = (1,1,1,1)
        _Glow ("Glow", Float) = 0
        _GroundY ("Ground Y", Float) = -10000
        _GlowHeight ("Glow Height", Float) = 0.1
        _Core ("Link Core", Float) = 0
        _CoreGain ("Link Core Gain", Float) = 1.35
        _VertexColor ("Use Vertex Color", Float) = 0
        _OutlineColor ("Outline Color", Color) = (0.05,0.035,0.035,1)
        _OutlineWidth ("Outline Pixels", Range(0,3)) = 1.4
        _HullWidth ("Hull Outline (m)", Float) = 0
        _Saturation ("Saturation", Range(0,1.5)) = 1
        _HighlightColor ("Steel Highlight", Color) = (0.45,0.5,0.58,1)
        _InnerGlow ("Link Inner Glow", Float) = 1
        _VeinGlow ("Link Vein Glow", Float) = 1
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half4 _Tint;
        half4 _ShadeColor;
        half _LightThreshold;
        half _LightFeather;
        half _AmbientWeight;
        half4 _EarthColor;
        half _Earth;
        half4 _GlowColor;
        half4 _HotColor;
        half4 _GlintColor;
        float _Glow;
        float _GroundY;
        float _GlowHeight;
        half _Core;
        half _CoreGain;
        half _VertexColor;
        half4 _OutlineColor;
        half _OutlineWidth;
        float _HullWidth;
        half _Saturation;
        half4 _HighlightColor;
        half _InnerGlow;
        half _VeinGlow;
    CBUFFER_END
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardIron"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                float3 normalOS : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half core = saturate(_Core);
                // У звена фактура — маска жил (трещины Hovl), не цвет: тело — тёмное железо _BaseColor.
                half3 albedo = lerp(tex.rgb, half3(1, 1, 1), core) * _BaseColor.rgb * _Tint.rgb;
                half useColor = step(0.5h, _VertexColor);
                albedo *= lerp(half3(1, 1, 1), input.color.rgb, useColor);
                // Ком земли: тёмная бурая земля, фактура камня — только светотенью.
                half luma = dot(tex.rgb, half3(0.3h, 0.55h, 0.15h));
                albedo = lerp(albedo, _EarthColor.rgb * (0.7h + 0.6h * luma), saturate(_Earth));
                // V6: камень серо-бурый, не бежевый — фактура арены приглушена к серому.
                half grey = dot(albedo, half3(0.3h, 0.55h, 0.15h));
                albedo = lerp(grey.xxx, albedo, _Saturation);

                float3 normalWS = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndl = dot(normalWS, mainLight.direction) * 0.5h + 0.5h;
                half lit = smoothstep(_LightThreshold - _LightFeather, _LightThreshold + _LightFeather, ndl)
                    * mainLight.shadowAttenuation;
                half3 direct = lerp(_ShadeColor.rgb, mainLight.color.rgb, lit);
                half3 color = albedo * (direct + SampleSH(normalWS) * _AmbientWeight);

                float3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half facing = saturate(dot(normalWS, viewWS));

                // Камень: короткий свет из трещины у подошвы.
                half under = saturate(1.0h - (input.positionWS.y - _GroundY) / max(0.01h, _GlowHeight));
                half power = max(0.0h, _Glow);
                half3 stoneGlow = _GlowColor.rgb * power * under * under;

                // Звено (V6): тёмное железо со стальными бликами и толстым контуром; голубой свет — только
                // изнутри: внутренняя сторона прута (к дыре звена), щель у земли и жилы-трещины в железе.
                float2 toAxis = -input.positionOS.xz;
                float axisLength = length(toAxis);
                float2 normalXZ = input.normalOS.xz;
                float normalLength = length(normalXZ);
                // Внутренняя сторона прута: горизонтальная доля нормали к оси звена (верх прута — 0, иначе вся
                // внутренняя половина верха светилась бы и звено читалось неоновой трубкой).
                half inner = axisLength > 1e-5 ? saturate(dot(toAxis / axisLength, normalXZ)) : 0.0h;
                half innerMask = smoothstep(0.35h, 0.92h, inner);
                float3 halfDir = normalize(mainLight.direction + viewWS);
                half nh = saturate(dot(normalWS, halfDir));
                half steel = smoothstep(0.9h, 0.985h, nh) * (0.3h + 0.7h * lit);
                half glint = smoothstep(0.75h, 0.95h, pow(nh, 64.0h));
                half top = saturate(normalWS.y * 1.6h - 0.2h);
                half vein = smoothstep(0.45h, 0.85h, tex.r) * top;
                half3 metal = color + _HighlightColor.rgb * steel * 0.6h;
                half seam = under * under * (0.25h + 0.75h * innerMask);
                half glowMask = innerMask * (0.55h + 0.45h * facing) + seam;
                half3 glowColor = lerp(_GlowColor.rgb, _HotColor.rgb, saturate(vein * 0.8h + seam * 0.4h));
                half3 linkGlow = glowColor * (glowMask * _InnerGlow + vein * _VeinGlow) * power * _CoreGain;
                linkGlow += _GlintColor.rgb * glint * saturate(power) * 0.6h;
                // Вспышка впечатывания (_Glow > 1): жилы и щель бледнеют к #9CD8FF, тело остаётся железом.
                linkGlow = lerp(linkGlow, _HotColor.rgb * power * _CoreGain * saturate(glowMask + vein),
                    saturate((power - 1.3h) * 0.5h) * 0.25h);
                half3 linkColor = metal * (1.0h - 0.4h * saturate(glowMask) * saturate(power)) + linkGlow;

                color = lerp(color + stoneGlow, linkColor, core);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            // Толстый тёмный контур «скорлупой»: вывернутая оболочка на _HullWidth метров (0 — нет).
            Name "IronHull"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HullVert
            #pragma fragment HullFrag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 smoothNormal : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half fogFactor : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings HullVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // V6: сглаженная нормаль из uv3 (гранёные плиты), иначе — своя (звено, старые меши).
                float3 hullNormal = dot(input.smoothNormal, input.smoothNormal) > 0.25 ? input.smoothNormal : input.normalOS;
                float3 normalWS = normalize(TransformObjectToWorldNormal(hullNormal));
                positionWS += normalWS * _HullWidth;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 HullFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(_HullWidth - 1e-4);
                return half4(MixFog(_OutlineColor.rgb, input.fogFactor), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "UnitOutlineMask"
            Tags { "LightMode"="UnitOutlineMask" }
            Cull Back
            ZTest LEqual
            ZWrite Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MaskVert
            #pragma fragment MaskFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings MaskVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 MaskFrag(Varyings input) : SV_Target
            {
                clip(_OutlineWidth - 0.001h);
                half width = saturate(_OutlineWidth / 3.0h);
                return half4(_OutlineColor.rgb * width, width);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float3 _LightDirection;
            float3 _LightPosition;

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(output.positionCS);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }
    }
}
