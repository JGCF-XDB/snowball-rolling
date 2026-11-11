Shader "SnowArena/IdentityOutline"
{
    Properties
    {
        _OutlineColor("Identity color", Color) = (1,0.08,0.12,1)
        _OutlineWidth("Shell width", Float) = 0.012
        _ContactPoint("Contact point (object space)", Vector) = (0,0,0,0)
        _ContactDepth("Contact dent depth", Float) = 0
        _ContactRadius("Contact dent radius", Float) = 1
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
                float4 _ContactPoint;
                float _ContactDepth, _ContactRadius;
            CBUFFER_END
            struct Attributes { float3 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; half fog:TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                // The shell has to dent exactly like the ball it wraps. It is drawn Cull Front
                // and is opaque, so a shell that stayed convex covered the dent and the hit read
                // as a coloured patch instead of a crater.
                float3 dented = input.positionOS;
                if (_ContactDepth > 0.0)
                {
                    float distanceToContact = distance(dented, _ContactPoint.xyz);
                    float radial = saturate(distanceToContact / max(_ContactRadius, 0.0001));
                    float falloff = 1.0 - radial * radial * (3.0 - 2.0 * radial);
                    dented -= normalize(dented) * (_ContactDepth * falloff);
                }
                float3 expanded = dented + normalize(dented) * _OutlineWidth;
                output.positionCS = TransformObjectToHClip(expanded);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input):SV_Target { return half4(MixFog(_OutlineColor.rgb,input.fog),1); }
            ENDHLSL
        }
    }
}
