Shader "SnowArena/IdentityOutline"
{
    Properties
    {
        _OutlineColor("Identity color", Color) = (1,0.08,0.12,1)
        _OutlineWidth("Shell width", Float) = 0.012
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END
            struct Attributes { float3 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; half fog:TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 expanded = input.positionOS+normalize(input.positionOS)*_OutlineWidth;
                output.positionCS = TransformObjectToHClip(expanded);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input):SV_Target { return half4(MixFog(_OutlineColor.rgb,input.fog),1); }
            ENDHLSL
        }
    }
}
