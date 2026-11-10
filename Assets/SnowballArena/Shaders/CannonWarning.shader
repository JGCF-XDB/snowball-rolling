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
                half core = 1 - smoothstep(.19 - aa, .19 + aa, distance);
                half halo = exp2(-6 * distance * distance) * (1 - smoothstep(.75, 1, distance));
                half alpha = saturate(core * .98 + halo * .22);
                half3 color = lerp(half3(.95, .004, .022), half3(1, .055, .09), core);
                float edgeFade = smoothstep(0, .003, input.uv.x) * smoothstep(0, .003, 1 - input.uv.x);
                return half4(color, alpha * edgeFade);
            }
            ENDHLSL
        }
    }
}
