// Particle effects (design/08 §1): unlit, texture red channel as mask, colour from the
// material (HDR, feeds bloom) times the particle vertex colour. Premultiplied output with
// _Opacity blending between additive (0) and alpha-blended (1): pure additive washes out to
// white on the light parchment board, so fire and gold keep some opacity to hold their colour.
// One blend state for every effect material; no lighting, no depth write: cheap enough for mobile.
Shader "Relics/Fx"
{
    Properties
    {
        _BaseMap ("Mask (R)", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _Opacity ("Opacity (0 additive, 1 alpha blend)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Color;
                half _Opacity;
            CBUFFER_END
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.color = i.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).r * i.color.a * _Color.a;
                return half4(_Color.rgb * i.color.rgb * a, a * _Opacity);
            }
            ENDHLSL
        }
    }
}
