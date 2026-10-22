Shader "Ummate/SpiritGrid/WaterSurface"
{
    Properties
    {
        _BaseColor ("Water Color", Color) = (0.055,0.3,0.36,1)
        _RippleColor ("Ripple Color", Color) = (0.18,0.53,0.57,1)
        _RippleScale ("Ripple Scale", Float) = 0.8
        _RippleSpeed ("Ripple Speed", Float) = 0.3
        _RippleStrength ("Ripple Strength", Range(0,0.2)) = 0.08
        _Smoothness ("Smoothness", Range(0,1)) = 0.85
        [HideInInspector] _Construction ("Construction", Float) = 0
    }
    SubShader
    {
        // Ground tint renders at 2002 without depth writes. Render water afterwards,
        // before transparent grid lines, so its colour cannot be covered by that tint's depth bias.
        // Depth writes still let props, characters and other opaque geometry occlude the water.
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+3" "RenderType"="Opaque" }
        Pass
        {
            Name "WaterForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _RippleColor;
            float _RippleScale, _RippleSpeed, _RippleStrength, _Smoothness, _Construction;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // World-space ripples share their phase across neighbouring Hex meshes.
                float2 p = input.positionWS.xz * _RippleScale;
                float t = _Time.y * _RippleSpeed;
                float a = p.x + p.y * 0.7 + t;
                float b = p.x * -0.45 + p.y * 1.3 - t * 0.8;
                float ripple = (sin(a) + sin(b)) * 0.25 + 0.5;
                half3 normalWS = normalize(half3(-cos(a) * _RippleStrength, 1,
                    -cos(b) * _RippleStrength));
                half3 view = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                Light light = GetMainLight();
                half3 halfway = SafeNormalize(light.direction + view);
                half specular = pow(saturate(dot(normalWS, halfway)), lerp(16, 128, _Smoothness));
                half3 albedo = lerp(_BaseColor.rgb, _RippleColor.rgb, ripple * 0.45);
                half3 illumination = SampleSH(normalWS) + light.color * saturate(dot(normalWS, light.direction));
                half3 color = albedo * max(illumination, half3(0.35,0.35,0.35)) + light.color * specular * 0.4;
                color = lerp(color, half3(0.36,0.4,0.4), saturate(_Construction) * 0.6);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
