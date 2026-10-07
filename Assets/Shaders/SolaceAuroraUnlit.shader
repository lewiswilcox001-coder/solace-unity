// Solace — additive aurora glow shader.
//
// Renders the aurora curtain mesh (AuroraView): per-vertex colors, additive
// blending so the veils glow over the night sky and stars. Deliberately:
//   - NO alpha blending, NO textures, NO keywords, NO fog code.
//   - Fixed Blend One One state only — zero URP variant risk.
// The night sky behind is near-black, so additive reads as soft light.
Shader "Solace/AuroraUnlit"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend One One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color : COLOR0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(IN.color.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
