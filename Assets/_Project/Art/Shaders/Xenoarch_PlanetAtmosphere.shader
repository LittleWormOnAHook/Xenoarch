// URP port of PlanetPack02 "Custom/Planets_Atmosphere": a slightly larger shell around the planet, alpha-blended,
// coloured from inner to outer atmosphere by view angle and faded out on the night side (URP main light).
Shader "Xenoarch/Space/PlanetAtmosphere"
{
    Properties
    {
        _Highatmospherecolor ("High atmosphere colour", Color) = (0.14, 0.21, 0.94, 0)
        _Lowatmospherecolor ("Low atmosphere colour", Color) = (0.26, 0.48, 1, 1)
        _Inneratmosphere ("Inner atmosphere", Color) = (0.56, 0.67, 1, 1)
        _Outeratmospherelimit ("Outer atmosphere limit", Range(0, 1)) = 0.735
        _Outeratmopsheredensity ("Outer atmosphere density", Range(0, 1)) = 1
        _Inneratmopsheredensity ("Inner atmosphere density", Range(0, 1)) = 0.19
        _Innerouterlimit ("Inner outer limit", Range(0, 1)) = 0.665
        _Inneroutersmoothness ("Inner outer smoothness", Range(0, 1)) = 0.43
        _Brightness ("Brightness", Range(0, 4)) = 1.2
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Highatmospherecolor; float4 _Lowatmospherecolor; float4 _Inneratmosphere;
                float _Outeratmospherelimit; float _Outeratmopsheredensity; float _Inneratmopsheredensity;
                float _Innerouterlimit; float _Inneroutersmoothness; float _Brightness;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 posWS : TEXCOORD0; float3 nWS : TEXCOORD1; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.posWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.nWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.nWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.posWS));
                Light L = GetMainLight();
                float3 l = normalize(L.direction);
                float ndl = dot(n, l);
                float w = 0.125;
                float3 diffuse = max(0, ndl * (1 - w) + w) * L.color;
                float fres = 1 - saturate(dot(n, v));
                float ramp1 = smoothstep(0, _Outeratmospherelimit, pow(fres, 7));
                float ramp2 = smoothstep(max(_Innerouterlimit - _Inneroutersmoothness, 0), min(_Innerouterlimit + _Inneroutersmoothness, 1), fres);
                float3 col = lerp(_Inneratmosphere.rgb, lerp(_Lowatmospherecolor.rgb, _Highatmospherecolor.rgb, ramp1), ramp2);
                float a = lerp(_Inneratmopsheredensity, (1 - ramp1) * _Outeratmopsheredensity, ramp2) * saturate(ndl * 2);
                return half4(diffuse * col * _Brightness, saturate(a));
            }
            ENDHLSL
        }
    }
}
