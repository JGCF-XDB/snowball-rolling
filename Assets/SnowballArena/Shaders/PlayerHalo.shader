Shader "SnowArena/PlayerHalo"
{
    Properties
    {
        _BaseColor ("Halo tint", Color) = (1, 0.42, 0.10, 0.55)
        _Radius ("Flare radius", Float) = 0.38
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            // Alpha blending, not additive. Additive saturates to white on snow, which is
            // exactly the arena floor, so the halo has to tint the ground instead of adding
            // light to it.
            Blend SrcAlpha OneMinusSrcAlpha
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
                // Solid over most of the disc so it reads as a coloured patch of ground rather
                // than a faint glow that snow can swallow. Only the last third fades out.
                half fade = 1 - smoothstep(0.55, 1, radius);
                return half4(_BaseColor.rgb, fade * _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
