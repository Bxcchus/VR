using System;
using System.IO;
using System.Linq;
using CampusExplorer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CampusExplorer.Editor
{
    // Evidence capture only. This never saves or changes the tour scene.
    [InitializeOnLoad]
    public static class ModernVillaFinalPolishCapture
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string RequestPath = "Evidence/final-polish/capture.request";
        const string OutputRoot = "Evidence/final-polish";
        const int VillaLayer = 31;

        static readonly (string key, string camera, float eyeY)[] Views =
        {
            ("kitchen", "cocina", 66.58f),
            ("living-room", "sala de estar", 66.58f),
            ("dining-room", "comedor", 66.58f),
            ("master-bedroom", "suit principal", 69.80f),
            ("bathroom", "baño principal", 69.80f),
            ("pool-terrace", "Camera.006", 66.50f),
            ("garden-pavilion", "Camera.007", 66.55f),
            ("exterior-front", "Camera.004", 67.00f),
            ("grass-close", null, 65.30f),
        };

        static ModernVillaFinalPolishCapture()
        {
            EditorApplication.update += Poll;
        }

        // Read-only capture from an isolated project copy for material comparison.
        public static void CaptureGrassTrialInBatch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Capture("grass-trial");
        }

        static void Poll()
        {
            if (!File.Exists(RequestPath) || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            var phase = File.ReadAllText(RequestPath).Trim();
            File.Delete(RequestPath);
            if (phase != "before" && phase != "after")
            {
                Debug.LogError("[Final Polish] Capture request must contain before or after.");
                return;
            }

            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                Debug.LogError("[Final Polish] Open ModernVillaTour in Edit Mode before capturing.");
                return;
            }

            try
            {
                Capture(phase);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        static void Capture(string phase)
        {
            var folder = Path.Combine(OutputRoot, phase);
            Directory.CreateDirectory(folder);
            var sourceCameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            foreach (var view in Views)
            {
                var source = view.camera == null ? null : sourceCameras.FirstOrDefault(candidate =>
                    string.Equals(candidate.name, view.camera, StringComparison.OrdinalIgnoreCase));
                if (source == null && view.camera != null)
                {
                    Debug.LogWarning("[Final Polish] Camera missing: " + view.camera);
                    continue;
                }

                var holder = new GameObject("Final polish evidence camera")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                try
                {
                    if (view.key == "grass-close")
                    {
                        var position = new Vector3(-10.5f, 65.30f, -12.8f);
                        var lookTarget = new Vector3(-10.5f, 64.72f, -15.5f);
                        holder.transform.SetPositionAndRotation(position,
                            Quaternion.LookRotation(lookTarget - position, Vector3.up));
                    }
                    else
                        holder.transform.SetPositionAndRotation(
                            new Vector3(source.transform.position.x, view.eyeY, source.transform.position.z),
                            source.transform.rotation);
                    var camera = holder.AddComponent<Camera>();
                    camera.enabled = false;
                    camera.fieldOfView = 70f;
                    camera.nearClipPlane = 0.05f;
                    camera.farClipPlane = 350f;
                    camera.cullingMask = 1 << VillaLayer;
                    camera.clearFlags = CameraClearFlags.Skybox;
                    camera.allowHDR = true;
                    camera.allowMSAA = true;
                    camera.renderingPath = RenderingPath.Forward;
                    var exposure = holder.AddComponent<ModernVillaExposure>();
                    exposure.exposureEV = 1.35f;
                    exposure.contrast = 0.98f;
                    exposure.saturation = 0.90f;
                    exposure.shadowLift = 0.028f;
                    exposure.whiteBalance = new Color(1.03f, 0.96f, 1.02f, 1f);

                    var prior = RenderTexture.active;
                    var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGBHalf);
                    var image = new Texture2D(1600, 900, TextureFormat.RGB24, false, false);
                    try
                    {
                        camera.targetTexture = target;
                        camera.Render();
                        RenderTexture.active = target;
                        image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                        image.Apply(false, false);
                        File.WriteAllBytes(Path.Combine(folder, view.key + ".png"), image.EncodeToPNG());
                    }
                    finally
                    {
                        camera.targetTexture = null;
                        RenderTexture.active = prior;
                        UnityEngine.Object.DestroyImmediate(image);
                        UnityEngine.Object.DestroyImmediate(target);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(holder);
                }
            }

            Debug.Log("[Final Polish] " + phase + " captures completed at " + folder);
        }
    }
}
