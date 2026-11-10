Shader "SnowArena/Selected01Sky"
{
    Properties { _Top("Zenith",Color)=(0.20,0.48,0.76,1) _Horizon("Horizon",Color)=(0.64,0.81,0.95,1) _Bottom("Canyon",Color)=(0.40,0.63,0.79,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Top, _Horizon, _Bottom;
            CBUFFER_END
            struct A { float3 positionOS:POSITION; };
            struct V { float4 positionCS:SV_POSITION; float3 direction:TEXCOORD0; };
            V Vert(A input) { V o; o.positionCS=TransformObjectToHClip(input.positionOS);o.direction=input.positionOS;return o; }
            half4 Frag(V input):SV_Target
            {
                float y=normalize(input.direction).y;
                half3 c=lerp(_Horizon.rgb,_Top.rgb,pow(saturate(y*1.7),0.7));
                c=lerp(c,_Bottom.rgb,saturate(-y*1.3));
                return half4(c,1);
            }
            ENDHLSL
        }
    }
}
