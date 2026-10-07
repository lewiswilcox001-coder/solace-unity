// Solace — fog-free minimal lambert shader with GPU instancing.
//
// Used for the drifting low-poly clouds: flat faceted shading from a single
// sun/moon direction plus ambient, no fog (clouds live where fog would eat
// them). C# drives _Color / _SunDir / _SunColor / _AmbColor per frame for
// time-of-day moods and lightning flashes.
Shader "Solace/SkyLambert"
{
    Properties
    {
        _Color ("Albedo", Color) = (1,1,1,1)
        _SunDir ("Sun Direction", Vector) = (0,1,0,0)
        _SunColor ("Sun Color", Color) = (1,1,1,1)
        _AmbColor ("Ambient", Color) = (0.4,0.4,0.4,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float3 _SunDir;
                float3 _SunColor;
                float3 _AmbColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                float4x4 objToWorld = GetObjectToWorldMatrix();
                float4 worldPos = mul(objToWorld, float4(IN.positionOS.xyz, 1.0));
                OUT.positionHCS = mul(GetWorldToHClipMatrix(), worldPos);
                OUT.normalWS = normalize(mul((float3x3)objToWorld, IN.normalOS));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half ndl = saturate(dot(normalize(IN.normalWS), normalize(_SunDir)));
                // Slight wrap so shadow sides stay readable, never pitch black.
                ndl = ndl * 0.85 + 0.15;
                half3 col = _Color.rgb * (_AmbColor + _SunColor * ndl);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
