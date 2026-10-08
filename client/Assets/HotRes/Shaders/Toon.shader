// Cartoon look for units (design/04 §3): two-tone ramp lighting, rim light, toon highlight,
// inverted-hull outline.
// Material variants (cost tiers, living state) share one base texture: with _Recolor = 1 the
// texture only supplies shading detail and the patina mask (green over red), and colours come
// from the material. _BodyLum / _SpotLum are the base texture's median linear luminance of body
// and patina, per character. Vertex colour carries body-part masks from
// tools/art/blender/quadruped_rig.py: R = position along the tail, G = tail, B = horn (or tusks).
// _VAT (crowd units, design/08 §2): no skinning; each vertex reads its baked position and normal
// from vertex animation textures (CrowdBake), per instance clip from _Clip, drawn instanced.
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
        _BodyLum ("Body Luminance", Range(0.01, 1)) = 0.19
        _SpotLum ("Spot Luminance", Range(0.01, 1)) = 0.125
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
        [Header(Crowd)]
        [Toggle(_VAT)] _Vat ("Vertex Animation Texture", Float) = 0
        [NoScaleOffset] _VatPos ("VAT Positions", 2D) = "black" {}
        [NoScaleOffset] _VatNrm ("VAT Normals", 2D) = "black" {}
        _VatFps ("VAT Frames Per Second", Float) = 30
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
            half _BodyLum;
            half _SpotLum;
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
            float _VatFps;
        CBUFFER_END
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_VatPos);
        TEXTURE2D(_VatNrm);

        // x first row of the clip, y its frame count, z start time (_Time.y), w 1 = loop, 0 = hold the last frame.
        UNITY_INSTANCING_BUFFER_START(Crowd)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Clip)
        UNITY_INSTANCING_BUFFER_END(Crowd)

        // Replaces the mesh's own position and normal with the baked frame, blended to the next one.
        void Crowd(uint id, inout float3 position, inout float3 normal)
        {
        #if defined(_VAT)
            float4 clip = UNITY_ACCESS_INSTANCED_PROP(Crowd, _Clip);
            float f = max(0, (_Time.y - clip.z) * _VatFps);
            f = clip.w > 0 ? fmod(f, clip.y) : min(f, clip.y - 1);
            float f0 = floor(f);
            float f1 = clip.w > 0 ? fmod(f0 + 1, clip.y) : min(f0 + 1, clip.y - 1);
            int2 a = int2(id, clip.x + f0), b = int2(id, clip.x + f1);
            position = lerp(LOAD_TEXTURE2D_LOD(_VatPos, a, 0).xyz, LOAD_TEXTURE2D_LOD(_VatPos, b, 0).xyz, f - f0);
            normal = lerp(LOAD_TEXTURE2D_LOD(_VatNrm, a, 0).xyz, LOAD_TEXTURE2D_LOD(_VatNrm, b, 0).xyz, f - f0);
        #endif
        }
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local_vertex _VAT
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; uint id : SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; half4 parts : TEXCOORD3; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                Crowd(i.id, i.positionOS.xyz, i.normalOS);
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
                half detail = min(dot(tex, half3(0.3, 0.59, 0.11)) / lerp(_BodyLum, _SpotLum, spot), 1.5);
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
            // Drawn by the "Toon Outline" renderer feature after all opaques, not in the forward
            // draw, so the forward and outline draws each batch (ProjectSetup.EnsureOutlinePass).
            Name "Outline"
            Tags { "LightMode" = "Outline" }
            Cull Front
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local_vertex _VAT

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; uint id : SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                Crowd(i.id, i.positionOS.xyz, i.normalOS);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz + normalize(i.normalOS) * _OutlineWidth);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }

        // Own shadow and depth passes instead of UsePass from URP Lit: a borrowed pass brings Lit's
        // UnityPerMaterial layout, which makes the whole shader SRP Batcher incompatible (design/08 §2).
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local_vertex _VAT

            // Set by URP's shadow caster pass for the main (directional) light.
            float3 _LightDirection;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; uint id : SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 Vert(Attributes i) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Crowd(i.id, i.positionOS.xyz, i.normalOS);
                float3 positionWS = TransformObjectToWorld(i.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(i.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local_vertex _VAT

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; uint id : SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 Vert(Attributes i) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Crowd(i.id, i.positionOS.xyz, i.normalOS);
                return TransformObjectToHClip(i.positionOS.xyz);
            }
            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
