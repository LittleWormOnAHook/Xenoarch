// ZEN-26: fine-scanner detector glow. Draws a buried find through the terrain (ZTest Always) as a soft cyan rim
// (fresnel outline), additive, low alpha; the pulse and strength come from SurfaceScannerTool via _Alpha.
Shader "Xenoarch/XRayGlow"
{
    Properties
    {
        _Color ("Color", Color) = (0.41, 0.87, 0.84, 1)
        _Alpha ("Alpha", Range(0, 1)) = 0.3
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "XRayGlow"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest Always
            ZWrite Off
            Cull Back
            Blend SrcAlpha One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Alpha;
                half _RimPower;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                half rim = pow(1.0 - saturate(abs(dot(n, v))), _RimPower);
                half a = saturate(_Alpha * (0.25 + 1.75 * rim));
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
}