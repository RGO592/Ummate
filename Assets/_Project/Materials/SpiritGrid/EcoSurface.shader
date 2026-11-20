Shader "Susupjung/SpiritGrid/SurfaceOverlay"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        [HideInInspector] _OverviewMark ("Map symbol and inverse cell width", Vector) = (0,0,0,0)
        [Toggle] _ZWrite ("Depth Write", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 tilePosition : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _OverviewMark;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.tilePosition = input.positionOS.xz * _OverviewMark.y;
                return output;
            }
            float Coverage(float signedDistance)
            {
                float halfPixel = max(0.5 * fwidth(signedDistance), 0.0002);
                return 1 - smoothstep(-halfPixel, halfPixel, signedDistance);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                if (_OverviewMark.x < 0.5) return _BaseColor;
                float2 p = input.tilePosition;
                float outer;
                half3 ink;
                if (_OverviewMark.x < 1.5)
                {
                    // Five round petals and a warm centre remain readable in a map view.
                    outer = 1;
                    [unroll] for (int i = 0; i < 5; ++i)
                    {
                        float angle = 1.25663706 * i;
                        float2 centre = 0.147 * float2(sin(angle), cos(angle));
                        outer = min(outer, length(p - centre) - 0.145);
                    }
                    ink = lerp(half3(1,0.91,0.77), half3(0.9,0.46,0.045), Coverage(length(p) - 0.087));
                }
                else
                {
                    float cap = max(length(float2(p.x, p.y * 1.15)) - 0.285, -p.y - 0.015);
                    float stem = length(max(abs(p - float2(0,-0.105)) - float2(0.053,0.083), 0)) - 0.034;
                    outer = min(cap, stem);
                    ink = lerp(half3(1,0.89,0.69), half3(0.7,0.13,0.06), Coverage(cap));
                    ink = lerp(ink, half3(1,0.85,0.64), Coverage(length(p - float2(-0.086,0.103)) - 0.038));
                }
                half3 color = lerp(_BaseColor.rgb, half3(0.19,0.105,0.08), Coverage(outer - 0.018));
                return half4(lerp(color, ink, Coverage(outer)), _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
