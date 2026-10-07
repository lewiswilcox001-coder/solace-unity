// Solace — photo mode color grade shader.
//
// Fullscreen color grade for photo mode filters: tint, saturation,
// contrast, brightness, and vignette. Used via Graphics.Blit from
// PhotoMode.cs. If this shader is missing, PhotoMode degrades gracefully
// to unfiltered capture.
Shader "Solace/PhotoGrade"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Saturation ("Saturation", Float) = 1.0
        _Contrast ("Contrast", Float) = 1.0
        _Brightness ("Brightness", Float) = 0.0
        _Vignette ("Vignette", Float) = 0.0
        _CvdMode ("Color Vision Deficiency", Float) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _Tint;
            float _Saturation;
            float _Contrast;
            float _Brightness;
            float _Vignette;
            float _CvdMode;

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            // Color vision deficiency simulation (Machado, Oliveira & Fernandes 2009).
            // 1 = Protanopia (red-blind), 2 = Deuteranopia (green-blind),
            // 3 = Tritanopia (blue-blind). Used for accessibility checking:
            // important colors should stay distinguishable under simulation.
            float3 ApplyCvd(float3 c, float mode)
            {
                if (mode < 0.5) return c;
                float3x3 m;
                if (mode < 1.5)
                {
                    // Protanopia.
                    m = float3x3(0.567, 0.433, 0.0,
                                 0.558, 0.442, 0.0,
                                 0.0,   0.242, 0.758);
                }
                else if (mode < 2.5)
                {
                    // Deuteranopia.
                    m = float3x3(0.625, 0.375, 0.0,
                                 0.7,   0.3,   0.0,
                                 0.0,   0.3,   0.7);
                }
                else
                {
                    // Tritanopia.
                    m = float3x3(0.95, 0.05,  0.0,
                                 0.0,  0.433, 0.567,
                                 0.0,  0.475, 0.525);
                }
                return mul(m, c);
            }

            float4 frag(Varyings i) : SV_Target
            {
                float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                // Saturation (luminance-preserving lerp toward grey).
                float lum = dot(c.rgb, float3(0.299, 0.587, 0.114));
                c.rgb = lerp(float3(lum, lum, lum), c.rgb, _Saturation);
                // Contrast + brightness around mid-grey.
                c.rgb = (c.rgb - 0.5) * _Contrast + 0.5 + _Brightness;
                // Tint multiply.
                c.rgb *= _Tint.rgb;
                // CVD simulation (accessibility).
                c.rgb = ApplyCvd(c.rgb, _CvdMode);
                // Vignette: darken toward the corners.
                float2 d = i.uv - 0.5;
                float v = 1.0 - _Vignette * dot(d, d) * 2.0;
                c.rgb *= clamp(v, 0.0, 1.0);
                return float4(c.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
