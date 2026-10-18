Shader "Ummate/SpiritGrid/GridLines"
{
    Properties
    {
        _BaseColor ("Line Color", Color) = (0.92,1,0.82,0.95)
        _CellSize ("Cell Size", Float) = 10
        _LineWidth ("Line Width", Float) = 0.14
        _BorderWidth ("Border Width", Float) = 0.22
        _MinPixelWidth ("Minimum Screen Width", Float) = 1.25
        _MapMin ("Map Minimum", Vector) = (-30,-30,0,0)
        _MapSize ("Map Size", Vector) = (216,216,0,0)
        _GridOrigin ("Grid Origin", Vector) = (-30,-30,0,0)
        _ActiveRect ("Active Rectangle", Vector) = (-30,-30,30,30)
    }
    SubShader
    {
        // Draw after the locked/active tint. One flat mesh avoids sorting intersections
        // between long X/Z strips; ordinary depth testing still respects world objects.
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 localGround : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor, _MapMin, _MapSize, _GridOrigin, _ActiveRect;
            float _CellSize, _LineWidth, _BorderWidth, _MinPixelWidth;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.localGround = _MapMin.xy + input.uv * _MapSize.xy;
                return output;
            }
            float Coverage(float distanceToLine, float unitsPerPixel, float width)
            {
                float pixel = max(unitsPerPixel, 0.00001);
                float halfWidth = max(width, pixel * _MinPixelWidth) * 0.5;
                return 1.0 - smoothstep(halfWidth - pixel * 0.5, halfWidth + pixel * 0.5, distanceToLine);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.localGround;
                float cell = max(_CellSize, 0.001);
                float2 pixel = max(fwidth(p), float2(0.00001,0.00001));
                float2 nearest = abs(frac((p - _GridOrigin.xy) / cell + 0.5) - 0.5) * cell;
                float lines = max(Coverage(nearest.x, pixel.x, _LineWidth), Coverage(nearest.y, pixel.y, _LineWidth));
                float2 edge = min(p - _MapMin.xy, _MapMin.xy + _MapSize.xy - p);
                float border = max(Coverage(edge.x, pixel.x, _BorderWidth * 2), Coverage(edge.y, pixel.y, _BorderWidth * 2));
                float2 q = abs(p - (_ActiveRect.xy + _ActiveRect.zw) * 0.5) - (_ActiveRect.zw - _ActiveRect.xy) * 0.5;
                float activeDistance = abs(length(max(q, 0.0)) + min(max(q.x, q.y), 0.0));
                float active = Coverage(activeDistance, max(pixel.x,pixel.y), _BorderWidth);
                return half4(_BaseColor.rgb, _BaseColor.a * max(lines, max(border, active)));
            }
            ENDHLSL
        }
    }
}
