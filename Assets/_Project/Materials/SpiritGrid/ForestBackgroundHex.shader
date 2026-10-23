Shader "Ummate/SpiritGrid/ForestBackgroundHex"
{
    Properties
    {
        _BaseColor ("Grass", Color) = (0.54,0.733,0.38,1)
        _LineColor ("Faint Hex Edge", Color) = (0.72,0.85,0.56,1)
        _Variation ("Grass Brightness Variation", Range(0,0.15)) = 0.045
        _LineStrength ("Edge Strength", Range(0,1)) = 0.28
        _LineWidth ("Edge Half Width", Float) = 0.035
        _HexWidth ("Hex Flat-to-flat Width", Float) = 10
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite On
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 cellOffset : TEXCOORD0; float variation : TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor, _LineColor;
            float _Variation, _LineStrength, _LineWidth, _HexWidth;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.cellOffset = input.uv;
                output.variation = input.color.r;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.cellOffset;
                float support = max(abs(p.x), max(abs(dot(p, float2(0.5,0.866025403784))),
                    abs(dot(p, float2(-0.5,0.866025403784)))));
                float edgeDistance = max(0, _HexWidth * 0.5 - support);
                float pixel = max(fwidth(edgeDistance), 0.00001);
                float width = max(_LineWidth, pixel * 0.35);
                float edgeCoverage = 1 - smoothstep(width - pixel * 0.5, width + pixel * 0.5, edgeDistance);
                float3 grass = _BaseColor.rgb * (1 + (input.variation * 2 - 1) * _Variation);
                return half4(lerp(grass, _LineColor.rgb, edgeCoverage * _LineStrength), 1);
            }
            ENDHLSL
        }
    }
}
