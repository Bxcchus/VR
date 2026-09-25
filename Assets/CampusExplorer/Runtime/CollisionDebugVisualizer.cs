using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace CampusExplorer
{
    /// <summary>
    /// Optional runtime/editor visualization for the invisible navigation helpers.
    /// The generated renderers are transient and are always hidden on startup.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CollisionDebugVisualizer : MonoBehaviour
    {
        [SerializeField] bool visibleOnStart;
        [SerializeField] Key toggleKey = Key.F8;

        readonly Dictionary<string, Material> materials = new();
        Transform visualsRoot;

        public bool IsVisible => visualsRoot != null && visualsRoot.gameObject.activeSelf;
        public Key ToggleKey => toggleKey;

        void Awake()
        {
            SetVisible(visibleOnStart);
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
                Toggle();
        }

        public void Toggle() => SetVisible(!IsVisible);

        public void SetVisible(bool visible)
        {
            if (visible)
            {
                Rebuild();
                visualsRoot.gameObject.SetActive(true);
            }
            else if (visualsRoot != null)
            {
                visualsRoot.gameObject.SetActive(false);
            }
        }

        public void Rebuild()
        {
            ClearVisuals();
            var root = new GameObject("Collision Debug Visuals");
            root.hideFlags = HideFlags.DontSave;
            visualsRoot = root.transform;
            visualsRoot.SetParent(transform, false);

            foreach (var box in EnumerateDebugColliders())
            {
                if (box.transform.IsChildOf(visualsRoot))
                    continue;

                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "Debug - " + box.name;
                visual.hideFlags = HideFlags.DontSave;
                visual.layer = 30;
                var generatedCollider = visual.GetComponent<Collider>();
                if (Application.isPlaying)
                    Destroy(generatedCollider);
                else
                    DestroyImmediate(generatedCollider);

                visual.transform.SetParent(visualsRoot, false);
                visual.transform.SetPositionAndRotation(box.transform.TransformPoint(box.center), box.transform.rotation);
                var scale = box.transform.lossyScale;
                visual.transform.localScale = Vector3.Scale(box.size,
                    new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

                var renderer = visual.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = MaterialFor(box.transform);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        IEnumerable<BoxCollider> EnumerateDebugColliders()
        {
            var environment = GetComponentsInChildren<BoxCollider>(true);
            var interactiveDoors = GameObject.Find("InteractiveDoors");
            if (interactiveDoors == null)
                return environment;

            // Door colliders live with their animated leaves so they follow the
            // hinge/slide motion. Include them in the same F8 audit view without
            // moving or duplicating their physical components.
            return environment
                .Concat(interactiveDoors.GetComponentsInChildren<BoxCollider>(true))
                .Distinct();
        }

        Material MaterialFor(Transform colliderTransform)
        {
            var path = HierarchyPath(colliderTransform);
            var category = "Furniture";
            var color = new Color(1f, 0.25f, 0.75f, 0.45f);
            if (path.Contains("InteractiveDoors")) { category = "Doors"; color = new Color(1f, 0.82f, 0.10f, 0.55f); }
            else if (path.Contains("/Floors")) { category = "Floors"; color = new Color(0.10f, 0.85f, 1f, 0.42f); }
            else if (path.Contains("/Walls")) { category = "Walls"; color = new Color(0.20f, 0.40f, 1f, 0.42f); }
            else if (path.Contains("/Doors")) { category = "Doors"; color = new Color(1f, 0.82f, 0.10f, 0.55f); }
            else if (path.Contains("/Staircase")) { category = "Staircase"; color = new Color(0.20f, 1f, 0.35f, 0.55f); }
            else if (path.Contains("/Railings")) { category = "Railings"; color = new Color(1f, 0.48f, 0.08f, 0.55f); }
            else if (path.Contains("/PoolDeck")) { category = "PoolDeck"; color = new Color(0.05f, 0.90f, 1f, 0.48f); }
            else if (path.Contains("/Garden")) { category = "Garden"; color = new Color(0.15f, 0.95f, 0.30f, 0.42f); }
            else if (path.Contains("/Cabin")) { category = "Cabin"; color = new Color(0.68f, 0.30f, 1f, 0.46f); }
            else if (path.Contains("/Transitions")) { category = "Transitions"; color = new Color(0.78f, 1f, 0.10f, 0.52f); }
            else if (path.Contains("/Pool") || path.Contains("/Boundaries")) { category = "Safety"; color = new Color(1f, 0.12f, 0.12f, 0.48f); }
            else if (path.Contains("/Terrace")) { category = "Terrace"; color = new Color(0.20f, 1f, 0.75f, 0.42f); }

            if (materials.TryGetValue(category, out var existing))
                return existing;

            // Clone a material already rendered correctly by this exact scene and
            // switch that clone to transparent. Shader.Find can return a shader
            // whose variant is unavailable on the active graphics API, which
            // makes every helper render Unity's bright error-purple colour.
            var template = FindObjectsByType<Renderer>(FindObjectsInactive.Include)
                .FirstOrDefault(renderer => renderer.name == "Floor_Kitchen_Tiles")?.sharedMaterial;
            if (template == null)
                throw new MissingReferenceException("No supported collision debug material is available.");
            var material = new Material(template);
            material.name = "Collision Debug " + category;
            material.hideFlags = HideFlags.DontSave;
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            materials[category] = material;
            return material;
        }

        void ClearVisuals()
        {
            if (visualsRoot == null)
                return;
            if (Application.isPlaying)
                Destroy(visualsRoot.gameObject);
            else
                DestroyImmediate(visualsRoot.gameObject);
            visualsRoot = null;
        }

        void OnDestroy()
        {
            ClearVisuals();
            foreach (var material in materials.Values)
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            materials.Clear();
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
