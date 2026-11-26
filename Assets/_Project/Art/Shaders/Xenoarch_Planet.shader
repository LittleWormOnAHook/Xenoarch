// URP port of PlanetPack02 "Custom/Planet_Base" (+ its cloud layer) for orbit views: lat-long albedo, normal,
// smoothness, AO and water mask maps, a scrolling cloud layer, sun lighting from the URP main light with a soft
// terminator, water glint and a thin atmospheric rim. Unlit pass with its own lighting (planets are far from local lights).
Shader "Xenoarch/Space/Planet"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [NoScaleOffset] _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0, 2)) = 0.4
        [NoScaleOffset] _Glossiness ("Smoothness map", 2D) = "black" {}
        _SmoothnessShift ("Smoothness shift", Range(-1, 1)) = 0
        [NoScaleOffset] _AOMap ("AO map", 2D) = "white" {}
        _AOintensity ("AO intensity", Range(0.2, 5)) = 1
        [NoScaleOffset] _WaterMask ("Water / specular mask", 2D) = "black" {}
        _WaterChannel ("Water mask channel weights (RGBA)", Vector) = (1,0,0,0)
        [NoScaleOffset] _CloudTex ("Clouds", 2D) = "black" {}
        _CloudChannel ("Cloud channel weights (RGBA; PlanetPack02 = alpha)", Vector) = (1,0,0,0)
        _CloudOpacity ("Cloud opacity", Range(0, 1)) = 0.85
        _CloudSpeed ("Cloud drift (u/sec)", Float) = 0.0008
        _RimColor ("Atmosphere rim", Color) = (0.35, 0.55, 1, 1)
        _RimPower ("Rim power", Range(0.5, 12)) = 4
        _RimIntensity ("Rim intensity", Range(0, 4)) = 0.8
        _Ambient ("Night side light", Range(0, 0.2)) = 0.01
        _Exposure ("Exposure", Range(0, 4)) = 1.2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Planet"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap); TEXTURE2D(_Glossiness); TEXTURE2D(_AOMap); TEXTURE2D(_WaterMask); TEXTURE2D(_CloudTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; float4 _Color; float _BumpScale; float _SmoothnessShift; float _AOintensity;
                float4 _WaterChannel; float4 _CloudChannel; float _CloudOpacity; float _CloudSpeed; float4 _RimColor; float _RimPower; float _RimIntensity; float _Ambient; float _Exposure;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 posWS : TEXCOORD1; float3 nWS : TEXCOORD2; float4 tWS : TEXCOORD3; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(i.normalOS, i.tangentOS);
                o.positionCS = p.positionCS; o.posWS = p.positionWS; o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.nWS = n.normalWS; o.tWS = float4(n.tangentWS, i.tangentOS.w * GetOddNegativeScale());
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb * _Color.rgb;
                float ao = pow(SAMPLE_TEXTURE2D(_AOMap, sampler_MainTex, uv).g, _AOintensity);
                float smooth = saturate(SAMPLE_TEXTURE2D(_Glossiness, sampler_MainTex, uv).g + _SmoothnessShift);
                float water = saturate(dot(SAMPLE_TEXTURE2D(_WaterMask, sampler_MainTex, uv), _WaterChannel));
                float3 tn = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_MainTex, uv), _BumpScale);
                float3 nG = normalize(i.nWS);
                float3 t = normalize(i.tWS.xyz); float3 b = cross(nG, t) * i.tWS.w;
                float3 n = normalize(tn.x * t + tn.y * b + tn.z * nG);

                Light L = GetMainLight();
                float3 l = normalize(L.direction);
                float3 v = normalize(GetWorldSpaceViewDir(i.posWS));
                float ndl = saturate(dot(n, l));
                float term = smoothstep(-0.08, 0.2, dot(nG, l));           // soft terminator on the geometric normal
                float3 h = normalize(l + v);
                float spec = pow(saturate(dot(n, h)), lerp(16, 256, smooth)) * smooth * (0.15 + 0.85 * water) * 1.5;

                float2 cuv = uv + float2(_Time.y * _CloudSpeed, 0);
                float cloud = saturate(dot(SAMPLE_TEXTURE2D(_CloudTex, sampler_MainTex, cuv), _CloudChannel) * _CloudOpacity);
                float3 lit = albedo * ao * (ndl * term) + spec * term;
                float3 cloudLit = cloud.xxx * saturate(dot(nG, l) * 1.1 + 0.05) * term;
                float3 c = lerp(lit, cloudLit, cloud) * L.color * _Exposure + albedo * _Ambient;

                float rim = pow(1 - saturate(dot(nG, v)), _RimPower) * _RimIntensity;
                c += _RimColor.rgb * rim * saturate(dot(nG, l) * 1.5 + 0.3) * L.color;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
