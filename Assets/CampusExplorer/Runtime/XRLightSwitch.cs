using System;
using UnityEngine;
using UnityEngine.UI;

namespace CampusExplorer
{
    public sealed class XRLightSwitch : MonoBehaviour
    {
        [Serializable]
        public sealed class EmissiveTarget
        {
            public Renderer renderer;
            public int materialIndex;

            [NonSerialized] public Material originalMaterial;
            [NonSerialized] public Material runtimeMaterial;
            [NonSerialized] public string emissionProperty;
            [NonSerialized] public Color onEmission;
            [NonSerialized] public bool emissionKeywordWasEnabled;

            public EmissiveTarget(Renderer targetRenderer, int targetMaterialIndex)
            {
                renderer = targetRenderer;
                materialIndex = targetMaterialIndex;
            }
        }

        [SerializeField] string roomName;
        [SerializeField] Light[] controlledLights;
        [SerializeField] EmissiveTarget[] emissiveTargets;
        [SerializeField] Button switchButton;
        [SerializeField] Image switchTrack;
        [SerializeField] RectTransform switchHandle;
        [SerializeField] Text stateLabel;

        static readonly Color OnTrackColor = new(0.72f, 0.51f, 0.20f, 1f);
        static readonly Color OffTrackColor = new(0.16f, 0.18f, 0.22f, 1f);

        public string RoomName => roomName;
        public Light[] ControlledLights => controlledLights;
        public EmissiveTarget[] EmissiveTargets => emissiveTargets;
        public Button SwitchButton => switchButton;
        public bool IsOn { get; private set; } = true;
        public int ToggleCount { get; private set; }

        public void Configure(string targetRoom, Light[] lights, EmissiveTarget[] targets, Button button, Image track,
            RectTransform handle, Text label)
        {
            roomName = targetRoom;
            controlledLights = lights;
            emissiveTargets = targets;
            switchButton = button;
            switchTrack = track;
            switchHandle = handle;
            stateLabel = label;
        }

        void Awake()
        {
            CreateRuntimeEmissiveMaterials();
            if (switchButton != null)
                switchButton.onClick.AddListener(Toggle);
            ApplyState(true, false);
        }

        void OnDestroy()
        {
            if (switchButton != null)
                switchButton.onClick.RemoveListener(Toggle);
            RestoreSourceMaterials();
        }

        public void Toggle()
        {
            ApplyState(!IsOn, true);
        }

        void ApplyState(bool on, bool countToggle)
        {
            IsOn = on;
            if (countToggle)
                ToggleCount++;

            if (controlledLights != null)
            {
                foreach (var roomLight in controlledLights)
                {
                    if (roomLight != null)
                        roomLight.enabled = on;
                }
            }

            if (emissiveTargets != null)
            {
                foreach (var target in emissiveTargets)
                {
                    if (target?.runtimeMaterial == null || string.IsNullOrEmpty(target.emissionProperty))
                        continue;
                    target.runtimeMaterial.SetColor(target.emissionProperty, on ? target.onEmission : Color.black);
                    if (on && target.emissionKeywordWasEnabled)
                        target.runtimeMaterial.EnableKeyword("_EMISSION");
                    else if (!on)
                        target.runtimeMaterial.DisableKeyword("_EMISSION");
                }
            }

            if (switchTrack != null)
                switchTrack.color = on ? OnTrackColor : OffTrackColor;
            if (switchHandle != null)
                switchHandle.anchoredPosition = new Vector2(0f, on ? 25f : -25f);
            if (stateLabel != null)
            {
                stateLabel.text = on ? "LIGHT ON" : "LIGHT OFF";
                stateLabel.color = on ? new Color(1f, 0.90f, 0.65f) : new Color(0.66f, 0.70f, 0.76f);
            }

            if (countToggle)
                Debug.Log($"[Interactive Lights] {roomName}: {(on ? "ON" : "OFF")}");
        }

        void CreateRuntimeEmissiveMaterials()
        {
            if (emissiveTargets == null)
                return;

            foreach (var target in emissiveTargets)
            {
                if (target?.renderer == null)
                    continue;
                var materials = target.renderer.sharedMaterials;
                if (target.materialIndex < 0 || target.materialIndex >= materials.Length || materials[target.materialIndex] == null)
                    continue;

                target.originalMaterial = materials[target.materialIndex];
                target.runtimeMaterial = new Material(target.originalMaterial)
                {
                    name = target.originalMaterial.name + " (" + roomName + " Interactive Instance)",
                    hideFlags = HideFlags.DontSave
                };
                target.emissionProperty = FindEmissionProperty(target.runtimeMaterial);
                if (!string.IsNullOrEmpty(target.emissionProperty))
                {
                    target.onEmission = target.runtimeMaterial.GetColor(target.emissionProperty);
                    target.emissionKeywordWasEnabled = target.runtimeMaterial.IsKeywordEnabled("_EMISSION");
                }
                materials[target.materialIndex] = target.runtimeMaterial;
                target.renderer.sharedMaterials = materials;
            }
        }

        void RestoreSourceMaterials()
        {
            if (emissiveTargets == null)
                return;

            foreach (var target in emissiveTargets)
            {
                if (target?.renderer == null || target.originalMaterial == null)
                    continue;
                var materials = target.renderer.sharedMaterials;
                if (target.materialIndex >= 0 && target.materialIndex < materials.Length)
                {
                    materials[target.materialIndex] = target.originalMaterial;
                    target.renderer.sharedMaterials = materials;
                }
                if (target.runtimeMaterial != null)
                    Destroy(target.runtimeMaterial);
            }
        }

        static string FindEmissionProperty(Material material)
        {
            var candidates = new[] { "_EmissionColor", "_EmissiveColor", "_EmissiveFactor", "emissiveFactor" };
            foreach (var candidate in candidates)
                if (material.HasColor(candidate))
                    return candidate;
            return null;
        }
    }
}
