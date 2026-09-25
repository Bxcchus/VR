sampler2D _MainTex;
sampler2D _BumpMap;
sampler2D _RoughnessMap;
sampler2D _MetallicMap;
sampler2D _SpecularMap;
sampler2D _OpacityMap;
sampler2D _OcclusionMap;

fixed4 _Color;
fixed4 _EmissionColor;
half _BumpScale;
half _Smoothness;
half _Metallic;
half _Opacity;
half _HasNormalMap;
half _HasRoughnessMap;
half _HasMetallicMap;
half _HasSpecularMap;
half _HasOpacityMap;
half _HasOcclusionMap;

struct Input
{
    float2 uv_MainTex;
};

void surf(Input input, inout SurfaceOutputStandardSpecular output)
{
    fixed4 baseSample = tex2D(_MainTex, input.uv_MainTex) * _Color;
    half roughness = lerp(1.0h - _Smoothness, tex2D(_RoughnessMap, input.uv_MainTex).r,
        saturate(_HasRoughnessMap));
    half metallic = lerp(_Metallic, tex2D(_MetallicMap, input.uv_MainTex).r,
        saturate(_HasMetallicMap));
    fixed3 dielectricSpecular = lerp(_SpecColor.rgb,
        tex2D(_SpecularMap, input.uv_MainTex).rgb, saturate(_HasSpecularMap));
    output.Albedo = baseSample.rgb * (1.0h - metallic);
    output.Specular = lerp(dielectricSpecular, baseSample.rgb, metallic);
    output.Smoothness = 1.0h - saturate(roughness);
    output.Occlusion = lerp(1.0h, tex2D(_OcclusionMap, input.uv_MainTex).r,
        saturate(_HasOcclusionMap));
    output.Emission = _EmissionColor.rgb;
    if (_HasNormalMap > 0.5h)
        output.Normal = UnpackScaleNormal(tex2D(_BumpMap, input.uv_MainTex), _BumpScale);
    half opacity = lerp(1.0h, tex2D(_OpacityMap, input.uv_MainTex).r, saturate(_HasOpacityMap));
    output.Alpha = baseSample.a * opacity * _Opacity;
}
