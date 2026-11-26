// Xenoarch sky for space and planet surfaces (URP). An equirectangular star map (the NASA SVS Deep Star Maps 2020
// Milky Way, celestial coordinates) under an optional thin atmosphere: zenith / horizon gradient, ground below the
// horizon, a glow around the sun (direction / colour from SunDirector globals) and stars fading where the sky is bright.
// Airless bodies / space: haze 0 = black sky with the Milky Way. Mars: dusty butterscotch haze, bluish glow around the sun.
Shader "Xenoarch/Skybox/Planet Sky"
{
    Properties
    {
        [NoScaleOffset] _StarTex ("Star map (equirect)", 2D) = "black" {}
        _StarExposure ("Star exposure", Range(0, 4)) = 1
        _StarTint ("Star tint", Color) = (1,1,1,1)
        _Rotation ("Star rotation (deg)", Range(0, 360)) = 0
        _Tilt ("Star tilt (deg)", Range(-90, 90)) = 0
        [NoScaleOffset] _NebulaTex ("Nebula overlay (equirect, optional)", 2D) = "black" {}
        _NebulaIntensity ("Nebula intensity", Range(0, 4)) = 0
        _HazeIntensity ("Atmosphere (0 = airless)", Range(0, 2)) = 0
        _ZenithColor ("Zenith colour", Color) = (0.55, 0.42, 0.32, 1)
        _HorizonColor ("Horizon colour", Color) = (0.85, 0.66, 0.48, 1)
        _HazeHeight ("Haze falloff", Range(0.05, 4)) = 0.6
        _GroundColor ("Below horizon", Color) = (0.25, 0.17, 0.12, 1)
        _SunGlowColor ("Glow around the sun", Color) = (0.75, 0.82, 0.95, 1)
        _SunGlowIntensity ("Sun glow intensity", Range(0, 8)) = 1
        _SunGlowPower ("Sun glow tightness", Range(1, 256)) = 24
        _StarFade ("Stars fade with sky brightness", Range(0, 40)) = 8
        _NightFloor ("Atmosphere at night (0-1)", Range(0, 1)) = 0.04
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_StarTex); SAMPLER(sampler_StarTex);
            TEXTURE2D(_NebulaTex); SAMPLER(sampler_NebulaTex);
            CBUFFER_START(UnityPerMaterial)
                float _StarExposure; float4 _StarTint; float _Rotation; float _Tilt; float _NebulaIntensity;
                float _HazeIntensity; float4 _ZenithColor; float4 _HorizonColor; float _HazeHeight; float4 _GroundColor;
                float4 _SunGlowColor; float _SunGlowIntensity; float _SunGlowPower; float _StarFade; float _NightFloor;
            CBUFFER_END
            float3 _XenoSunDir;   // toward the sun (SunDirector)
            float4 _XenoSunColor;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.dir = i.positionOS.xyz;
                return o;
            }

            float3 RotY(float3 d, float deg) { float a = radians(deg), s = sin(a), c = cos(a); return float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z); }
            float3 RotX(float3 d, float deg) { float a = radians(deg), s = sin(a), c = cos(a); return float3(d.x, c * d.y - s * d.z, s * d.y + c * d.z); }

            float2 Equirect(float3 d)
            {
                float lat = acos(clamp(d.y, -1, 1));
                float lon = atan2(d.z, d.x);
                return float2(0.5, 1.0) - float2(lon, lat) * float2(0.5 / PI, 1.0 / PI);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 sd = RotY(RotX(d, _Tilt), _Rotation);
                float2 uv = Equirect(sd);
                float3 stars = SAMPLE_TEXTURE2D_LOD(_StarTex, sampler_StarTex, uv, 0).rgb * _StarTint.rgb * _StarExposure;
                stars += SAMPLE_TEXTURE2D_LOD(_NebulaTex, sampler_NebulaTex, uv, 0).rgb * _NebulaIntensity;

                float3 sun = normalize(_XenoSunDir.xyz + float3(0, 1e-4, 0));
                float3 sunCol = _XenoSunColor.a > 0 ? _XenoSunColor.rgb : float3(1, 1, 1);
                float mu = saturate(dot(d, sun));
                float3 atm = 0;
                if (_HazeIntensity > 0)
                {
                    float h = d.y;
                    float day = lerp(_NightFloor, 1.0, smoothstep(-0.12, 0.25, sun.y));
                    float t = pow(saturate(h), _HazeHeight);
                    float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, t);
                    sky = h < 0 ? lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-h * 6)) : sky;
                    float glow = pow(mu, _SunGlowPower) * _SunGlowIntensity + pow(mu, 4) * 0.25 * _SunGlowIntensity;
                    atm = (sky * day + _SunGlowColor.rgb * sunCol * glow * day) * _HazeIntensity;
                }
                float lum = dot(atm, float3(0.2126, 0.7152, 0.0722));
                float starVis = saturate(1 - lum * _StarFade);
                return half4(atm + stars * starVis, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
