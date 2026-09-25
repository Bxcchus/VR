Shader "Campus Explorer/Modern Villa PBR/Opaque Regional Material"
{
    Properties
    {
        _Color ("Base Color", Color) = (1,1,1,1)
        _MainTex ("Base Color Map", 2D) = "white" {}
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0,10)) = 1
        _RoughnessMap ("Roughness Map", 2D) = "white" {}
        _MetallicMap ("Metallic Map", 2D) = "black" {}
        _SpecularMap ("Specular Map", 2D) = "white" {}
        _OpacityMap ("Opacity Map", 2D) = "white" {}
        _OcclusionMap ("Occlusion Map", 2D) = "white" {}
        _IndoorTex ("Indoor Base Color Map", 2D) = "white" {}
        [Normal] _IndoorBumpMap ("Indoor Normal Map", 2D) = "bump" {}
        _IndoorSpecularMap ("Indoor Specular Map", 2D) = "white" {}
        _IndoorOcclusionMap ("Indoor Occlusion Map", 2D) = "white" {}
        _IndoorColor ("Indoor Base Color", Color) = (0.8,0.8,0.8,1)
        _IndoorSmoothness ("Indoor Smoothness", Range(0,1)) = 0.5
        _IndoorBumpScale ("Indoor Normal Scale", Range(0,4)) = 0
        _SpecColor ("Specular Color", Color) = (0.04,0.04,0.04,1)
        _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0
        _Opacity ("Opacity", Range(0,1)) = 1
        _GrassDetailStrength ("Grass Detail Strength", Range(0,1)) = 1
        _ClipMinX ("Clip Min X", Float) = -12.65
        _ClipMaxX ("Clip Max X", Float) = -6.0
        _ClipMinZ ("Clip Min Z", Float) = -36.2
        _ClipMaxZ ("Clip Max Z", Float) = -32.2
        [HideInInspector] _HasNormalMap ("Has Normal", Float) = 0
        [HideInInspector] _HasRoughnessMap ("Has Roughness", Float) = 0
        [HideInInspector] _HasMetallicMap ("Has Metallic", Float) = 0
        [HideInInspector] _HasSpecularMap ("Has Specular", Float) = 0
        [HideInInspector] _HasOpacityMap ("Has Opacity", Float) = 0
        [HideInInspector] _HasOcclusionMap ("Has Occlusion", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf StandardSpecular fullforwardshadows addshadow

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _RoughnessMap;
        sampler2D _MetallicMap;
        sampler2D _SpecularMap;
        sampler2D _OpacityMap;
        sampler2D _OcclusionMap;
        sampler2D _IndoorTex;
        sampler2D _IndoorBumpMap;
        sampler2D _IndoorSpecularMap;
        sampler2D _IndoorOcclusionMap;

        fixed4 _Color;
        fixed4 _EmissionColor;
        fixed4 _IndoorColor;
        half _BumpScale;
        half _Smoothness;
        half _Metallic;
        half _Opacity;
        half _GrassDetailStrength;
        half _HasNormalMap;
        half _HasRoughnessMap;
        half _HasMetallicMap;
        half _HasSpecularMap;
        half _HasOpacityMap;
        half _HasOcclusionMap;
        half _IndoorSmoothness;
        half _IndoorBumpScale;
        float _ClipMinX;
        float _ClipMaxX;
        float _ClipMinZ;
        float _ClipMaxZ;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_IndoorTex;
            float3 worldPos;
        };

        void surf(Input input, inout SurfaceOutputStandardSpecular output)
        {
            half insideX = step(_ClipMinX, input.worldPos.x) * step(input.worldPos.x, _ClipMaxX);
            half insideZ = step(_ClipMinZ, input.worldPos.z) * step(input.worldPos.z, _ClipMaxZ);
            half indoor = insideX * insideZ;

            float2 grassUvB = float2(-input.uv_MainTex.y, input.uv_MainTex.x) * 0.73 + float2(0.37, 0.19);
            fixed4 grassSampleA = tex2D(_MainTex, input.uv_MainTex);
            fixed4 grassSampleB = tex2D(_MainTex, grassUvB);
            fixed4 grassSource = lerp(grassSampleA, grassSampleB, 0.32h);
            half grassLuminance = dot(grassSource.rgb, fixed3(0.2126h, 0.7152h, 0.0722h));
            half lawnVariation = lerp(1.0h, saturate(0.72h + grassLuminance * 1.25h),
                saturate(_GrassDetailStrength));
            fixed4 grassSample = fixed4(_Color.rgb * lawnVariation, grassSource.a * _Color.a);
            fixed4 indoorSample = tex2D(_IndoorTex, input.uv_IndoorTex) * _IndoorColor;
            fixed4 baseSample = lerp(grassSample, indoorSample, indoor);
            half roughness = lerp(1.0h - _Smoothness, tex2D(_RoughnessMap, input.uv_MainTex).r,
                saturate(_HasRoughnessMap));
            half metallic = lerp(_Metallic, tex2D(_MetallicMap, input.uv_MainTex).r,
                saturate(_HasMetallicMap));
            fixed3 dielectricSpecular = lerp(_SpecColor.rgb,
                tex2D(_SpecularMap, input.uv_MainTex).rgb, saturate(_HasSpecularMap));
            output.Albedo = baseSample.rgb * (1.0h - metallic);
            fixed3 indoorSpecular = tex2D(_IndoorSpecularMap, input.uv_IndoorTex).rgb;
            output.Specular = lerp(lerp(dielectricSpecular, baseSample.rgb, metallic), indoorSpecular, indoor);
            output.Smoothness = lerp(1.0h - saturate(roughness), _IndoorSmoothness, indoor);
            half grassOcclusion = lerp(1.0h, tex2D(_OcclusionMap, input.uv_MainTex).r,
                saturate(_HasOcclusionMap));
            output.Occlusion = lerp(grassOcclusion, tex2D(_IndoorOcclusionMap, input.uv_IndoorTex).r, indoor);
            output.Emission = _EmissionColor.rgb;
            fixed3 grassNormalA = UnpackScaleNormal(tex2D(_BumpMap, input.uv_MainTex), _BumpScale);
            fixed3 grassNormalB = UnpackScaleNormal(tex2D(_BumpMap, grassUvB), _BumpScale * 0.7h);
            fixed3 grassNormal = normalize(lerp(grassNormalA, grassNormalB, 0.28h));
            fixed3 indoorNormal = UnpackScaleNormal(tex2D(_IndoorBumpMap, input.uv_IndoorTex), _IndoorBumpScale);
            if (_HasNormalMap > 0.5h || indoor > 0.5h)
                output.Normal = normalize(lerp(grassNormal, indoorNormal, indoor));
            half opacity = lerp(1.0h, tex2D(_OpacityMap, input.uv_MainTex).r, saturate(_HasOpacityMap));
            output.Alpha = baseSample.a * opacity * _Opacity;
        }
        ENDCG
    }
    Fallback "Standard (Specular setup)"
}
