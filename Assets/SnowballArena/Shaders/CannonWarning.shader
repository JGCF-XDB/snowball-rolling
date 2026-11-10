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
                // 0.19 matches CannonWarningVisual.CoreFraction. The core keeps
                // the vivid red of the approved thin warning; from its edge the
                // color eases into light red while the alpha falls away, so the
                // whole ribbon reads as one natural red gradient.
                half gradient = smoothstep(.19, 1, distance);
                half3 color = lerp(half3(.95, .004, .022), half3(1, .32, .36), gradient);
                half alpha = pow(1 - gradient, .8);
                float edgeFade = smoothstep(0, .003, input.uv.x) * smoothstep(0, .003, 1 - input.uv.x);
                return half4(color, alpha * edgeFade);
            }
            ENDHLSL
        }
    }
}
