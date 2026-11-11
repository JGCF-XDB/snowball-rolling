Shader "SnowArena/FacetedSnowLit"
{
    Properties
    {
        _BaseColor("Tint", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.12
        _Metallic("Metallic", Range(0,1)) = 0
        _SnowDetail("Snow grain", Range(0,1)) = 0
        _GroundTracks("Arena snow surface", Range(0,1)) = 0
        _IsIce("Glacier surface", Range(0,1)) = 0
        _ValleyFogStart("Valley fog height", Float) = -5
        _ValleyFogDensity("Valley fog density", Float) = 0.025
        _ContactPoint("Contact point (object space)", Vector) = (0,0,0,0)
        _ContactDepth("Contact dent depth", Float) = 0
        _ContactRadius("Contact dent radius", Float) = 1
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _ZWrite("ZWrite", Float) = 1
        [HideInInspector] _SrcBlend("Source blend", Float) = 1
        [HideInInspector] _DstBlend("Destination blend", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" "UniversalMaterialType"="Lit" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Smoothness, _Metallic, _SnowDetail, _GroundTracks, _IsIce;
            float _ValleyFogStart, _ValleyFogDensity;
            float4 _ContactPoint;
            float _ContactDepth, _ContactRadius;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half4 color : COLOR;
            half fog : TEXCOORD2;
            float3 positionOS : TEXCOORD3;
            half dent : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            // A local dent: vertices near the contact point are pushed toward the ball centre,
            // so the impact deforms one face while the silhouette keeps its size. Scaling the
            // whole transform (the old approach) read as the ball shrinking instead.
            float3 objectPosition = input.positionOS.xyz;
            float3 objectNormal = input.normalOS;
            half dent = 0;
            if (_ContactDepth > 0.0)
            {
                float distanceToContact = distance(objectPosition, _ContactPoint.xyz);
                // Smooth radial falloff. A squared falloff made the dent very local but left the
                // rim untouched, so the silhouette never changed and the result read as shading
                // rather than deformation.
                float radial = saturate(distanceToContact / max(_ContactRadius, 0.0001));
                float falloff = 1.0 - radial * radial * (3.0 - 2.0 * radial);
                float depth = _ContactDepth * falloff;
                objectPosition -= normalize(objectPosition) * depth;
                // Normals tilt partway toward the contact: enough to catch light on the rim,
                // not so much that the patch turns into a flat dark stain.
                float3 towardContact = normalize(_ContactPoint.xyz - objectPosition);
                objectNormal = normalize(lerp(objectNormal, towardContact, saturate(falloff * 0.55)));
                dent = saturate(falloff * saturate(_ContactDepth * 3.0));
            }
            VertexPositionInputs position = GetVertexPositionInputs(objectPosition);
            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.positionOS = objectPosition;
            output.dent = dent;
            output.normalWS = TransformObjectToWorldNormal(objectNormal);
            output.color = input.color;
            output.fog = ComputeFogFactor(position.positionCS.z);
            return output;
        }
        float Hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
        float Noise(float2 p)
        {
            float2 i = floor(p), f = frac(p); f = f*f*(3-2*f);
            return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
        }
        float SnowArc(float2 p, float2 center, float radius, float phase)
        {
            p -= center;
            float angle = atan2(p.y,p.x);
            float offset = 0.065*sin(angle*3+phase)+0.035*sin(angle*7-phase);
            float distance = abs(length(p)-radius-offset);
            float aa = max(fwidth(distance),0.012);
            float groove = 1-smoothstep(0.035-aa,0.065+aa,distance);
            float shoulder = 1-smoothstep(0.08,0.19,distance);
            float arc = smoothstep(-0.7,0.45,sin(angle+phase));
            return (groove*0.65+shoulder*0.35)*arc;
        }
        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half3 normal = normalize(input.normalWS);
            float3 grainPosition = lerp(input.positionOS*4,input.positionWS,_GroundTracks);
            float grain = Noise(grainPosition.xz*27.0+grainPosition.y*3.4);
            float drift = Noise(grainPosition.xz*0.7+grainPosition.y)*0.6+Noise(grainPosition.xz*2.8)*0.4;
            half3 albedo = input.color.rgb * _BaseColor.rgb * (1.0 - input.dent * 0.22);
            albedo *= 1 + _SnowDetail * ((grain-0.5)*0.045+(drift-0.5)*0.07);
            if (_GroundTracks > 0.5 && normal.y > 0.8)
            {
                float2 p = input.positionWS.xz;
                float tracks = SnowArc(p,float2(-0.3,0.35),3.6,0.4);
                tracks += SnowArc(p,float2(-0.3,0.35),3.82,0.7)*0.6;
                tracks += SnowArc(p,float2(0.2,-0.5),5.45,1.7);
                tracks += SnowArc(p,float2(0.2,-0.5),5.75,1.9)*0.65;
                tracks += SnowArc(p,float2(-0.8,0.3),7.65,2.8);
                tracks += SnowArc(p,float2(-0.8,0.3),7.89,3.0)*0.7;
                tracks += SnowArc(p,float2(0.6,0.4),8.9,4.2)*0.45;
                float2 wind = float2(p.x+p.y*0.38,p.y-p.x*0.38);
                float ripple = Noise(float2(wind.x*.9,wind.y*4+Noise(p*.55)*3));
                float powder = .55*ripple+.45*drift;
                albedo *= lerp(half3(.74,.83,.98),half3(1.05,1.06,1.08),powder);
                albedo *= 1-saturate(tracks)*0.22;
                float3 pressedSnow = input.positionWS - float3(0,tracks*.044-powder*.038,0);
                half3 trackNormal = normalize(cross(ddy(pressedSnow),ddx(pressedSnow)));
                if (dot(trackNormal,normal) < 0) trackNormal = -trackNormal;
                normal = normalize(lerp(normal,trackNormal,0.65));
            }
            SurfaceData surface = (SurfaceData)0;
            surface.albedo = albedo;
            surface.alpha = 1;
            surface.normalTS = half3(0,0,1);
            surface.metallic = _Metallic;
            surface.smoothness = _Smoothness + (grain-0.5)*_SnowDetail*0.035;
            surface.occlusion = 1;
            InputData lighting = (InputData)0;
            lighting.positionWS = input.positionWS;
            lighting.positionCS = input.positionCS;
            lighting.normalWS = normal;
            lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            lighting.bakedGI = SampleSH(normal) + half3(.10,.18,.28);
            lighting.vertexLighting = VertexLighting(input.positionWS,normal);
            lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            lighting.shadowMask = half4(1,1,1,1);
            half4 result = UniversalFragmentPBR(lighting,surface);
            float fresnel = pow(1-saturate(dot(normal,lighting.viewDirectionWS)),3);
            result.rgb += _IsIce * fresnel * half3(0.10,0.19,0.23);
            result.rgb = MixFog(result.rgb,input.fog);
            float valley = 1-exp(-max(0,_ValleyFogStart-input.positionWS.y)*_ValleyFogDensity);
            result.rgb = lerp(result.rgb,unity_FogColor.rgb,valley);
            result.a = lerp(1,_BaseColor.a*(0.74+0.24*fresnel),_IsIce);
            return result;
        }
        half4 DepthNormals(Varyings input) : SV_Target
        {
            half3 normal = normalize(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(normal)*0.5+0.5;
                return half4(PackFloat2To888(saturate(oct)),0);
            #else
                return half4(normal,0);
            #endif
        }
        float3 _LightDirection;
        Varyings ShadowVert(Attributes input)
        {
            Varyings output = Vert(input);
            output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS,normalize(output.normalWS),_LightDirection));
            #if UNITY_REVERSED_Z
                output.positionCS.z = min(output.positionCS.z,UNITY_NEAR_CLIP_VALUE*output.positionCS.w);
            #else
                output.positionCS.z = max(output.positionCS.z,UNITY_NEAR_CLIP_VALUE*output.positionCS.w);
            #endif
            return output;
        }
        half4 DepthFrag(Varyings input) : SV_Target { return 0; }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
}
