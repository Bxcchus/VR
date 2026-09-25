Shader "Hidden/Campus Explorer/Modern Villa Exposure"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _ExposureEV ("Exposure EV", Float) = 1
        _Contrast ("Contrast", Float) = 1.02
        _Saturation ("Saturation", Float) = 0.98
        _ShadowLift ("Shadow Lift", Float) = 0.02
        _WhiteBalance ("White Balance", Color) = (1,1,1,1)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _ExposureEV;
            float _Contrast;
            float _Saturation;
            float _ShadowLift;
            float4 _WhiteBalance;

            fixed4 frag(v2f_img input) : SV_Target
            {
                float3 color = max(0.0, tex2D(_MainTex, input.uv).rgb);
                color *= _WhiteBalance.rgb;
                color *= exp2(_ExposureEV);
                // Exponential shoulder keeps emissive strips and specular highlights controlled.
                color = 1.0 - exp(-color);
                float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
                color += _ShadowLift * (1.0 - smoothstep(0.02, 0.42, luminance));
                color = lerp(luminance.xxx, color, _Saturation);
                color = (color - 0.5) * _Contrast + 0.5;
                return fixed4(saturate(color), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
