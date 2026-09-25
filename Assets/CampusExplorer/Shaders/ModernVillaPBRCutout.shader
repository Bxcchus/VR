Shader "Campus Explorer/Modern Villa PBR/Cutout"
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
        _SpecColor ("Specular Color", Color) = (0.04,0.04,0.04,1)
        _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0
        _Opacity ("Opacity", Range(0,1)) = 1
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.35
        [HideInInspector] _HasNormalMap ("Has Normal", Float) = 0
        [HideInInspector] _HasRoughnessMap ("Has Roughness", Float) = 0
        [HideInInspector] _HasMetallicMap ("Has Metallic", Float) = 0
        [HideInInspector] _HasSpecularMap ("Has Specular", Float) = 0
        [HideInInspector] _HasOpacityMap ("Has Opacity", Float) = 0
        [HideInInspector] _HasOcclusionMap ("Has Occlusion", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        LOD 300
        Cull Off
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf StandardSpecular fullforwardshadows alphatest:_Cutoff addshadow
        #include "ModernVillaPBRShared.cginc"
        ENDCG
    }
    Fallback "Transparent/Cutout/VertexLit"
}
