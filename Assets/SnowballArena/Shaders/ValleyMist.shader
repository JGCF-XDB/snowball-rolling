Shader "SnowArena/ValleyMist"
{
    Properties { _Color("Cloud color",Color)=(0.72,0.86,0.96,0.42) _Seed("Cloud seed",Float)=0 _Billboard("Cloud wisp",Float)=0 _CloudTex("Baked volume cloud",2D)="white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+15" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_CloudTex); SAMPLER(sampler_CloudTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Seed, _Billboard;
            CBUFFER_END
            struct A { float3 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float2 uv:TEXCOORD1; };
            V Vert(A input) { V o; o.positionWS=TransformObjectToWorld(input.positionOS); o.positionCS=TransformWorldToHClip(o.positionWS); o.uv=input.uv; return o; }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p) { float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y); }
            half4 Frag(V input):SV_Target
            {
                float2 p=lerp(input.positionWS.xz*0.10,input.uv*4,_Billboard)+_Seed+float2(_Time.y*0.009,_Time.y*0.003);
                float cloud=Noise(p)*0.6+Noise(p*2.1)*0.28+Noise(p*4.1)*0.12;
                float edge=saturate(min(min(input.uv.x,1-input.uv.x),min(input.uv.y,1-input.uv.y))*10);
                edge*=lerp(1,1-smoothstep(0.20,0.62,length((input.uv-0.5)*float2(1,1.2))),_Billboard);
                float2 screenUV=GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneDepth=LinearEyeDepth(SampleSceneDepth(screenUV),_ZBufferParams);
                float thisDepth=-TransformWorldToView(input.positionWS).z;
                float soft=saturate((sceneDepth-thisDepth)*0.35);
                float2 cloudUV=input.uv;
                cloudUV.x=lerp(cloudUV.x,1-cloudUV.x,step(0.5,frac(_Seed*0.123)));
                half4 volume=SAMPLE_TEXTURE2D(_CloudTex,sampler_CloudTex,cloudUV);
                float density=lerp(smoothstep(0.25,0.75,cloud)*edge,volume.a,_Billboard)*soft*_Color.a;
                half3 bakedLight=lerp(half3(1.05,1.12,1.18),volume.rgb*2.2,0.45);
                half3 color=lerp(_Color.rgb,_Color.rgb*bakedLight,_Billboard);
                float valley=1-exp(-max(0,-6-input.positionWS.y)*0.012);
                return half4(lerp(color,unity_FogColor.rgb,valley),density);
            }
            ENDHLSL
        }
    }
}
