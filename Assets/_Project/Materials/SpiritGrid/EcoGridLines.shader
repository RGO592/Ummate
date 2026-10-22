Shader "Ummate/SpiritGrid/GridLines"
{
    Properties
    {
        _BaseColor ("Line Color", Color) = (0.92,1,0.82,0.95)
        _ActiveColor ("Unlocked Ground", Color) = (0.54,0.733,0.38,1)
        _LockedColor ("Locked Ground", Color) = (0.294,0.388,0.267,1)
        _HexWidth ("Hex Flat-to-flat Width", Float) = 10
        _LineWidth ("Line Width", Float) = 0.14
        _BorderWidth ("Border Width", Float) = 0.22
        _MinPixelWidth ("Minimum Screen Width", Float) = 1.25
        _MapMin ("Map Local Minimum", Vector) = (-5,-5.773503,0,0)
        _MapSize ("Map Local Size", Vector) = (215,210.732,0,0)
        _MapDimensions ("Offset Columns / Rows", Vector) = (21,24,0,0)
        _CellState ("Map State (R unlocked, G present)", 2D) = "black" {}
        _FillOnly ("Ground Fill Pass", Float) = 0
    }
    SubShader
    {
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
            TEXTURE2D(_CellState); SAMPLER(sampler_CellState);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor, _ActiveColor, _LockedColor, _MapMin, _MapSize, _MapDimensions;
            float4 _HexDirections[6]; // Uploaded from HexCoord.Directions, never independently ordered.
            float _HexWidth, _LineWidth, _BorderWidth, _MinPixelWidth, _FillOnly;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Quad UV encodes GridRoot local X/Z, including its rotation and scale.
                output.localGround = _MapMin.xy + input.uv * _MapSize.xy;
                return output;
            }
            float2 HexCenter(float2 coord)
            {
                return float2(_HexWidth * (coord.x + coord.y * 0.5), _HexWidth * 0.8660254037844386 * coord.y);
            }
            float2 WorldHex(float2 p)
            {
                float radius = _HexWidth / 1.7320508075688772;
                float2 qr = float2((0.5773502691896257 * p.x - p.y / 3.0) / radius, (2.0 / 3.0 * p.y) / radius);
                float3 cube = float3(qr, -qr.x - qr.y);
                float3 rounded = round(cube);
                float3 error = abs(rounded - cube);
                if (error.x > error.y && error.x > error.z) rounded.x = -rounded.y - rounded.z;
                else if (error.y > error.z) rounded.y = -rounded.x - rounded.z;
                return rounded.xy;
            }
            float2 State(float2 hex)
            {
                // Odd-row offset, identical to HexMetrics.OffsetColumn, including negative rows.
                float2 cell = float2(hex.x + floor(hex.y * 0.5), hex.y);
                if (any(cell < 0) || any(cell >= _MapDimensions.xy)) return 0;
                return SAMPLE_TEXTURE2D_LOD(_CellState, sampler_CellState, (cell + 0.5) / _MapDimensions.xy, 0).rg;
            }
            float Coverage(float distanceToLine, float pixel, float width)
            {
                float halfWidth = max(width, pixel * _MinPixelWidth) * 0.5;
                return 1.0 - smoothstep(halfWidth - pixel * 0.5, halfWidth + pixel * 0.5, distanceToLine);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 hex = WorldHex(input.localGround);
                float2 state = State(hex);
                clip(state.y - 0.5);
                if (_FillOnly > 0.5) return lerp(_LockedColor, _ActiveColor, state.x);
                float2 offset = input.localGround - HexCenter(hex);
                float pixel = max(length(fwidth(input.localGround)), 0.00001);
                float lines = 0;
                [unroll] for (int d = 0; d < 6; d++)
                {
                    float2 normal = normalize(HexCenter(_HexDirections[d].xy));
                    float distanceToEdge = max(0, _HexWidth * 0.5 - dot(offset, normal));
                    float2 other = State(hex + _HexDirections[d].xy);
                    float boundary = max(abs(state.x - other.x), 1.0 - other.y);
                    float width = lerp(_LineWidth, _BorderWidth, boundary);
                    lines = max(lines, Coverage(distanceToEdge, pixel, width));
                }
                return half4(_BaseColor.rgb, _BaseColor.a * lines);
            }
            ENDHLSL
        }
    }
}
