Shader "Alpha Striker/Factory Dynamic Sky"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Panoramic Clouds", 2D) = "grey" {}
        _Exposure ("Sky Exposure", Range(0, 2)) = 0.53
        [HDR] _SunColor ("Sun Disk", Color) = (4.5, 3.9, 2.9, 1)
        [HDR] _HaloColor ("Sun Halo", Color) = (1.0, 0.66, 0.35, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half _Exposure;
            half4 _SunColor;
            half4 _HaloColor;

            // URP selects the scene's RenderSettings.sun as its main directional light.
            float4 _MainLightPosition;
            half4 _MainLightColor;

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(float4 position : POSITION)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(position);
                output.direction = position.xyz;
                return output;
            }

            // Radial profiles use one minus the cosine of the angular separation.
            float Glow(float radius, float separation)
            {
                float falloff = saturate(1.0 - separation / radius);
                return falloff * falloff;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float longitude = atan2(direction.z, direction.x);
                float latitude = acos(clamp(direction.y, -1.0, 1.0));
                float2 panoramaUV = float2(0.5 - longitude * (0.5 / UNITY_PI), 1.0 - latitude / UNITY_PI);
                float2 uvDx = ddx(panoramaUV);
                float2 uvDy = ddy(panoramaUV);
                // Keep the mip level continuous where longitude wraps.
                uvDx.x -= round(uvDx.x);
                uvDy.x -= round(uvDy.x);
                half3 clouds = tex2Dgrad(_MainTex, panoramaUV, uvDx, uvDy).rgb;

                // Project an upper-cloud patch over the compressed rows at the zenith.
                float2 zenithUV = float2(0.67, 0.77) + direction.xz * 0.28;
                half3 zenithClouds = tex2D(_MainTex, zenithUV).rgb;
                clouds = lerp(clouds, zenithClouds, smoothstep(0.91, 0.985, direction.y));
                half3 sky = clouds * _Exposure;

                float3 sunDirection = _MainLightPosition.xyz;
                float separation = max(0.0, 1.0 - dot(direction, sunDirection));
                float daylight = smoothstep(0.0, 0.07, sunDirection.y) *
                    saturate(max(_MainLightColor.r, max(_MainLightColor.g, _MainLightColor.b)));
                // The disk feather runs from 0.95 to 1.2 degrees off its center.
                float disk = 1.0 - smoothstep(0.000137, 0.000219, separation);
                float halo = 0.82 * Glow(0.0062, separation) + 0.33 * Glow(0.0437, separation);
                sky += daylight * (disk * _SunColor.rgb + halo * _HaloColor.rgb);
                return half4(sky, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
