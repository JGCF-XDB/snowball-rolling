Shader "SnowArena/CannonWarning"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float d = abs(input.uv.y * 2 - 1);
                float u = input.uv.x;
                float t = _Time.y;

                // Vivid core of the approved warning. Its edge is deliberately
                // wide and smooth so it does not read as a painted stripe.
                half core = 1 - smoothstep(.12, .21, d);

                // Two-stage glow. The wide tail is multiplied by a long fade so
                // the alpha reaches zero exactly at the ribbon border with zero
                // slope: the light melts into the snow instead of ending on a rim.
                half glowNear = exp2(-5.0 * d * d);
                half glowWide = exp2(-1.9 * d * d);
                half tail = 1 - smoothstep(.30, 1.0, d);

                // Projected-on-snow texture: low frequency energy along the lane,
                // edge grain and a slight width wobble. The core stays clean so the
                // warning remains readable.
                half n = sin(u * 9.0 + 1.3) * .55 + sin(u * 21.0 + 3.7) * .3 + sin(u * 47.0 + 1.1) * .15;
                float wobble = (sin(u * 13.0) * .5 + sin(u * 31.0 + 2.0) * .5) * .010;
                float dw = abs((input.uv.y + wobble) * 2 - 1);
                half grain = 1 - .20 * smoothstep(.62, 1, dw);
                half band = (.92 + .08 * n) * grain;

                // Slow breathing plus one soft highlight travelling along the lane.
                // Only brightness moves, never the hue or the gameplay geometry.
                half breath = 1 + .07 * sin(t * 2.0);
                float sweepPos = frac(t * 0.4);
                half sweep = exp2(-90.0 * (u - sweepPos) * (u - sweepPos)) * .18;
                half energy = breath * (1 + sweep);

                half alpha = saturate(core + (glowNear * .45 + glowWide * .50) * band * energy) * tail;

                // Dither the faint tail so the gradient does not band on the snow.
                float dither = frac(sin(dot(input.positionCS.xy, float2(12.9898, 78.233))) * 43758.5453) - .5;
                alpha += dither / 255.0;

                half ends = smoothstep(0, .035, u) * smoothstep(0, .035, 1 - u);
                half3 color = lerp(half3(.95, .004, .022), half3(1, .30, .33), smoothstep(.08, .70, d));
                return half4(color, alpha * ends);
            }
            ENDHLSL
        }
    }
}
