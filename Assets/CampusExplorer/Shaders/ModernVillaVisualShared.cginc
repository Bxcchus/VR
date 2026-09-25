sampler2D _MainTex;
sampler2D _BumpMap;
sampler2D _RoughnessMap;
sampler2D _MetallicMap;
sampler2D _SpecularMap;
sampler2D _OpacityMap;
sampler2D _OcclusionMap;
sampler2D _EmissionMap;

fixed4 _Color;
fixed4 _EmissionColor;
half _BumpScale;
half _Smoothness;
half _Metallic;
half _Opacity;
half _EmissionIntensity;
half _RoughnessMapIsGloss;
half _SmoothnessScale;
half _SmoothnessBias;
half _SpecularScale;
half _OcclusionStrength;
half _HasNormalMap;
half _HasRoughnessMap;
half _HasMetallicMap;
half _HasSpecularMap;
half _HasOpacityMap;
half _HasOcclusionMap;
half _HasEmissionMap;

struct Input
{
    float2 uv_MainTex;
};

void surf(Input input, inout SurfaceOutputStandardSpecular output)
{
    fixed4 baseSample = tex2D(_MainTex, input.uv_MainTex) * _Color;
    half surfaceMap = tex2D(_RoughnessMap, input.uv_MainTex).r;
    half mappedSmoothness = lerp(1.0h - surfaceMap, surfaceMap, saturate(_RoughnessMapIsGloss));
    mappedSmoothness = saturate(mappedSmoothness * _SmoothnessScale + _SmoothnessBias);
    half smoothness = lerp(_Smoothness, mappedSmoothness, saturate(_HasRoughnessMap));
    half metallic = lerp(_Metallic, tex2D(_MetallicMap, input.uv_MainTex).r,
        saturate(_HasMetallicMap));
    fixed3 dielectricSpecular = lerp(_SpecColor.rgb,
        tex2D(_SpecularMap, input.uv_MainTex).rgb, saturate(_HasSpecularMap)) * _SpecularScale;
    fixed3 emissionTexture = lerp(fixed3(1, 1, 1),
        tex2D(_EmissionMap, input.uv_MainTex).rgb, saturate(_HasEmissionMap));

    output.Albedo = baseSample.rgb * (1.0h - metallic);
    output.Specular = lerp(dielectricSpecular, baseSample.rgb, metallic);
    output.Smoothness = saturate(smoothness);
    half mappedOcclusion = lerp(1.0h, tex2D(_OcclusionMap, input.uv_MainTex).r,
        saturate(_HasOcclusionMap));
    output.Occlusion = lerp(1.0h, mappedOcclusion, saturate(_OcclusionStrength));
    output.Emission = _EmissionColor.rgb * emissionTexture * _EmissionIntensity;
    if (_HasNormalMap > 0.5h)
        output.Normal = UnpackScaleNormal(tex2D(_BumpMap, input.uv_MainTex), _BumpScale);
    half opacity = lerp(1.0h, tex2D(_OpacityMap, input.uv_MainTex).r, saturate(_HasOpacityMap));
    output.Alpha = saturate(baseSample.a * opacity * _Opacity);
}
