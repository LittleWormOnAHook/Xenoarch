// Sun disc + corona for SunDirector: an additive camera-facing quad 10 disc diameters wide, placed far along the sun
// direction (depth-tested, so the hull / terrain hides it; no fog). Limb-darkened HDR disc so bloom picks it up.
Shader "Xenoarch/SunDisc"
{
    Properties
    {
        [HDR] _Color ("Colour (HDR)", Color) = (24, 23, 21, 1)
        _Glow ("Corona strength", Range(0, 4)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-50" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color; float _Glow;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = i.uv; return o; }
            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv - 0.5) * 10.0;            // in disc radii (quad = 10 diameters -> edge at r = 5)
                float edge = fwidth(r) + 0.02;
                float disc = 1 - smoothstep(1 - edge, 1 + edge, r);
                float mu = sqrt(saturate(1 - r * r));
                float limb = 1 - 0.55 * (1 - mu);              // limb darkening
                float corona = (exp(-max(r - 1, 0) * 2.6) * 0.05 + exp(-r * 0.8) * 0.012) * _Glow * saturate((5 - r) / 1.5);
                float3 c = _Color.rgb * (disc * limb + corona * (1 - disc));
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
