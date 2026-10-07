// Particle effects (design/08 §1): unlit, red channel of the shared mask atlas as mask, colour
// from the particle vertex colour times an intensity (HDR, feeds bloom). Premultiplied output
// with an opacity blending between additive (0) and alpha-blended (1): pure additive washes out
// to white on the light parchment board, so fire and gold keep some opacity to hold their colour.
// Intensity and opacity come per particle (custom data, TEXCOORD0.zw), so every effect layer
// shares one material and batches (art/fx/README.md §5). No lighting, no depth write.
Shader "Relics/Fx"
{
    Properties
    {
        _BaseMap ("Mask (R)", 2D) = "white" {}
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
            CBUFFER_END
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // uv: atlas uv in xy, intensity and opacity in zw.
            struct Attributes { float4 positionOS : POSITION; float4 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 uv : TEXCOORD0; half4 color : COLOR; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = float4(TRANSFORM_TEX(i.uv.xy, _BaseMap), i.uv.zw);
                o.color = i.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv.xy).r * i.color.a;
                return half4(i.uv.z * i.color.rgb * a, a * i.uv.w);
            }
            ENDHLSL
        }
    }
}
