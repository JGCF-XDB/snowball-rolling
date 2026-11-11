Shader "SnowArena/SnowExplosion"
{
    // Shared unlit shader for every optional layer of the snowball explosion.
    // Quads use their UVs for a soft radial sprite; volume meshes point UVs at
    // the texture centre and fade through vertex alpha instead. Blend mode is a
    // material property so one shader can serve both the alpha-blended powder
    // and the additive flash / shockwave / glints.
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Softness ("Radial edge softness", Range(0.01, 1)) = 0.75
        _Power ("Radial falloff", Range(0.1, 8)) = 1.8
        _SrcBlend ("Source blend", Float) = 5
        _DstBlend ("Destination blend", Float) = 10
        _Cull ("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        ZTest LEqual
        Cull [_Cull]
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Softness;
                float _Power;
                float _SrcBlend;
                float _DstBlend;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Soft circular mask. A quad's UV spans the whole 1x1 sprite, so the
                // centre is opaque and the rim reaches zero exactly at the edge.
                float2 p = input.uv * 2.0 - 1.0;
                float radius = saturate(length(p));
                half falloff = pow(saturate(1.0 - radius), _Power);
                half edge = 1.0 - smoothstep(1.0 - _Softness, 1.0, radius);
                half alpha = input.color.a * _Color.a * falloff * edge;

                // Dither the faint rim so the sprite does not band on the bright snow.
                float dither = frac(sin(dot(input.positionCS.xy, float2(12.9898, 78.233))) * 43758.5453) - 0.5;
                alpha += dither / 255.0;

                half3 color = input.color.rgb * _Color.rgb;
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
