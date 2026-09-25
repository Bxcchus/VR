Shader "Campus Explorer/Modern Villa Visual/Cutout"
{
    Properties
    {
        _Color ("Base Color", Color) = (1,1,1,1)
        _MainTex ("Base Color Map", 2D) = "white" {}
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0,4)) = 1
        _RoughnessMap ("Roughness or Gloss Map", 2D) = "white" {}
        _MetallicMap ("Metallic Map", 2D) = "black" {}
        _SpecularMap ("Specular Map", 2D) = "white" {}
        _OpacityMap ("Opacity Map", 2D) = "white" {}
        _OcclusionMap ("Ambient Occlusion Map", 2D) = "white" {}
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _SpecColor ("Specular Color", Color) = (0.04,0.04,0.04,1)
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _EmissionIntensity ("Emission Intensity", Range(0,8)) = 0
        _Smoothness ("Fallback Smoothness", Range(0,1)) = 0.25
        _Metallic ("Fallback Metallic", Range(0,1)) = 0
        _Opacity ("Opacity", Range(0,1)) = 1
        _SmoothnessScale ("Smoothness Scale", Range(0,2)) = 1
        _SmoothnessBias ("Smoothness Bias", Range(-1,1)) = 0
        _SpecularScale ("Specular Scale", Range(0,2)) = 1
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.35
        [HideInInspector] _RoughnessMapIsGloss ("Map Is Gloss", Float) = 0
        [HideInInspector] _HasNormalMap ("Has Normal", Float) = 0
        [HideInInspector] _HasRoughnessMap ("Has Roughness", Float) = 0
        [HideInInspector] _HasMetallicMap ("Has Metallic", Float) = 0
        [HideInInspector] _HasSpecularMap ("Has Specular", Float) = 0
        [HideInInspector] _HasOpacityMap ("Has Opacity", Float) = 0
        [HideInInspector] _HasOcclusionMap ("Has Occlusion", Float) = 0
        [HideInInspector] _HasEmissionMap ("Has Emission", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        LOD 300
        Cull Off
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf StandardSpecular fullforwardshadows alphatest:_Cutoff addshadow
        #include "ModernVillaVisualShared.cginc"
        ENDCG
    }
    Fallback "Transparent/Cutout/VertexLit"
}
