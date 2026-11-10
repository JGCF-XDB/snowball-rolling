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
                float distance = abs(input.uv.y * 2 - 1);
                float aa = max(fwidth(distance), .025);
                // The dark core covers 0.19 of the half width, matching
                // CannonWarningVisual.CoreFraction.
                half core = 1 - smoothstep(.19 - aa, .19 + aa, distance);
                // Across the width, a dark red center blends into pale red
                // shoulders, which fade smoothly into the snow on both sides.
                half shoulder = 1 - smoothstep(0, 1, distance);
                half alpha = saturate(core * .4 + shoulder * .58);
                half gradient = smoothstep(.10, .90, distance);
                half3 color = lerp(half3(.55, .004, .018), half3(1, .36, .42), gradient);
                float edgeFade = smoothstep(0, .003, input.uv.x) * smoothstep(0, .003, 1 - input.uv.x);
                return half4(color, alpha * edgeFade);
            }
            ENDHLSL
        }
    }
}
