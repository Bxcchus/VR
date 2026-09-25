using UnityEngine;

namespace CampusExplorer
{
    /// <summary>
    /// Small built-in-pipeline exposure pass used by the visual review cameras.
    /// It lifts the baked night interior while compressing fixture highlights.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public sealed class ModernVillaExposure : MonoBehaviour
    {
        [Range(-2f, 3f)] public float exposureEV = 1f;
        [Range(0.8f, 1.2f)] public float contrast = 1.02f;
        [Range(0f, 1.2f)] public float saturation = 0.98f;
        [Range(0f, 0.08f)] public float shadowLift = 0.02f;
        public Color whiteBalance = Color.white;

        Material material;

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            var shader = Shader.Find("Hidden/Campus Explorer/Modern Villa Exposure");
            if (shader == null || !shader.isSupported)
            {
                Graphics.Blit(source, destination);
                return;
            }

            if (material == null || material.shader != shader)
            {
                if (material != null) DestroyImmediate(material);
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            material.SetFloat("_ExposureEV", exposureEV);
            material.SetFloat("_Contrast", contrast);
            material.SetFloat("_Saturation", saturation);
            material.SetFloat("_ShadowLift", shadowLift);
            material.SetColor("_WhiteBalance", whiteBalance);
            Graphics.Blit(source, destination, material);
        }

        void OnDisable()
        {
            if (material == null) return;
            DestroyImmediate(material);
            material = null;
        }
    }
}
