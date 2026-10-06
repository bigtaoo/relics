// Cartoon look for units (design/04 §3): two-tone ramp lighting, rim light, toon highlight,
// inverted-hull outline.
// Material variants (cost tiers, living state) share one base texture: with _Recolor = 1 the
// texture only supplies shading detail and the patina mask (green over red), and colours come
// from the material. Vertex colour carries body-part masks from tools/art/blender/rig_zheng.py:
// R = position along the tail, G = tail, B = horn.
Shader "Relics/Toon"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shade Color", Color) = (0.55, 0.5, 0.62, 1)
        _ShadeThreshold ("Shade Threshold", Range(-1, 1)) = 0.05
        _ShadeSoftness ("Shade Softness", Range(0.001, 0.5)) = 0.04
        _SpecColor ("Highlight Color", Color) = (0, 0, 0, 1)
        _SpecSize ("Highlight Size", Range(0, 0.5)) = 0.08
        [Header(Variant)]
        _Recolor ("Recolor", Range(0, 1)) = 0
        _BodyColor ("Body", Color) = (1, 1, 1, 1)
        _SpotColor ("Spots", Color) = (0.5, 0.5, 0.5, 1)
        _HornColor ("Horn", Color) = (1, 1, 1, 1)
        _TailRootColor ("Tail Root", Color) = (1, 1, 1, 1)
        _TailTipColor ("Tail Tip", Color) = (1, 1, 1, 1)
        _TailGlow ("Tail Glow", Range(0, 3)) = 0
        _RimColor ("Rim Color", Color) = (1, 0.92, 0.7, 1)
        _RimPower ("Rim Power", Range(1, 10)) = 4
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.35
        _OutlineColor ("Outline Color", Color) = (0.13, 0.09, 0.05, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.03)) = 0.006
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadeColor;
            half _ShadeThreshold;
            half _ShadeSoftness;
            half4 _SpecColor;
            half _SpecSize;
            half _Recolor;
            half4 _BodyColor;
            half4 _SpotColor;
            half4 _HornColor;
            half4 _TailRootColor;
            half4 _TailTipColor;
            half _TailGlow;
            half4 _RimColor;
            half _RimPower;
            half _RimStrength;
            half4 _OutlineColor;
            float _OutlineWidth;
        CBUFFER_END
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; half4 parts : TEXCOORD3; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.parts = i.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                // Recolour: patina spots are green over red; luminance relative to the median of
                // body / spots (linear) keeps the sculpted shading and crevices.
                half spot = smoothstep(-0.03, 0.03, tex.g - tex.r);
                half detail = min(dot(tex, half3(0.3, 0.59, 0.11)) / lerp(0.19, 0.125, spot), 1.5);
                half tailPos = saturate((i.parts.r - 0.125) / 0.75);
                half3 tail = lerp(_TailRootColor.rgb, _TailTipColor.rgb, tailPos);
                half3 tint = lerp(_BodyColor.rgb, _SpotColor.rgb, spot);
                tint = lerp(tint, _HornColor.rgb, i.parts.b);
                tint = lerp(tint, tail, i.parts.g);
                half3 albedo = lerp(tex, tint * detail, _Recolor) * _BaseColor.rgb;
                half3 glow = tail * (i.parts.g * tailPos * _TailGlow * _Recolor);

                float3 n = normalize(i.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(n, light.direction);
                half lit = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, ndl) * light.shadowAttenuation;
                half3 col = lerp(albedo * _ShadeColor.rgb, albedo, lit) * light.color;
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                col += pow(1 - saturate(dot(n, v)), _RimPower) * _RimStrength * lit * _RimColor.rgb;
                half spec = smoothstep(1 - _SpecSize - 0.01, 1 - _SpecSize + 0.01, dot(n, normalize(light.direction + v)));
                col += spec * lit * _SpecColor.rgb;
                col += albedo * SampleSH(n) * 0.3 + glow;
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz + normalize(i.normalOS) * _OutlineWidth);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
