using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CampusExplorer.Editor
{
    public static class ModernVillaEmissionAudit
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";

        [MenuItem("Campus Explorer/Interaction/Audit Fixture Emission")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var interactiveRoot = GameObject.Find("InteractiveLights");
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include)
                .Where(renderer => interactiveRoot == null || !renderer.transform.IsChildOf(interactiveRoot.transform))
                .OrderBy(renderer => renderer.bounds.center.y)
                .ThenBy(renderer => renderer.name)
                .ToArray();

            foreach (var renderer in renderers)
            {
                for (var slot = 0; slot < renderer.sharedMaterials.Length; slot++)
                {
                    var material = renderer.sharedMaterials[slot];
                    if (material == null)
                        continue;

                    var emissionProperty = FindEmissionProperty(material);
                    var materialName = material.name.ToLowerInvariant();
                    var objectName = renderer.name.ToLowerInvariant();
                    var looksLikeFixture = materialName.Contains("light") || materialName.Contains("lamp") ||
                                           materialName.Contains("emit") || materialName.Contains("led") ||
                                           objectName.Contains("light") || objectName.Contains("lamp") ||
                                           objectName.Contains("spot") || objectName.Contains("led");
                    if (emissionProperty == null && !looksLikeFixture)
                        continue;

                    var emission = emissionProperty == null ? Color.black : material.GetColor(emissionProperty);
                    Debug.Log($"[Interactive Lights][Emission Audit] path={HierarchyPath(renderer.transform)} | " +
                              $"center={renderer.bounds.center} size={renderer.bounds.size} layer={renderer.gameObject.layer} | " +
                              $"slot={slot} material={material.name} shader={material.shader.name} | " +
                              $"emissionProperty={emissionProperty ?? "none"} emission={emission} " +
                              $"keyword={material.IsKeywordEnabled("_EMISSION")} giFlags={material.globalIlluminationFlags}");
                }
            }

            Debug.Log($"[Interactive Lights][Emission Audit] Inspected {renderers.Length} villa renderers.");
        }

        static string FindEmissionProperty(Material material)
        {
            var names = new[] { "_EmissionColor", "_EmissiveColor", "_EmissiveFactor", "emissiveFactor" };
            return names.FirstOrDefault(material.HasColor);
        }

        static string HierarchyPath(Transform item)
        {
            var path = item.name;
            while (item.parent != null)
            {
                item = item.parent;
                path = item.name + "/" + path;
            }
            return path;
        }
    }
}
