Shader "SnowArena/CannonFlash"
{
    Properties
    {
        _BaseColor ("Orange glow", Color) = (1, 0.5, 0.12, 1)
        _Radius ("Flare radius", Float) = 0.38
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Radius;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 radial : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.radial = input.positionOS.xy / max(_Radius, 0.001);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float radius = length(input.radial);
                half fade = 1 - smoothstep(0.15, 1, radius);
                half3 glow = lerp(half3(1, 0.85, 0.45), _BaseColor.rgb, smoothstep(0, 0.7, radius));
                return half4(glow * 1.7, fade * fade * _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
