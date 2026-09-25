using System;
using System.IO;
using System.Linq;
using CampusExplorer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CampusExplorer.Editor
{
    public static class ModernVillaInteractiveLightCapture
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";

        sealed class View
        {
            public readonly string room;
            public readonly string slug;
            public readonly Vector3 position;
            public readonly Vector3 target;

            public View(string room, string slug, Vector3 position, Vector3 target)
            {
                this.room = room;
                this.slug = slug;
                this.position = position;
                this.target = target;
            }
        }

        static readonly View[] Views =
        {
            new("Kitchen", "kitchen", new Vector3(2.15f, 66.53f, -25.65f), new Vector3(-1.20f, 66.55f, -25.65f)),
            new("Living Room", "living-room", new Vector3(-4.20f, 66.35f, -27.20f), new Vector3(-9.10f, 66.10f, -22.50f)),
            new("Master Bedroom", "master-bedroom", new Vector3(2.20f, 69.72f, -28.95f), new Vector3(-0.45f, 69.30f, -31.25f)),
        };

        public static void BuildAndCapture()
        {
            ModernVillaInteractiveLightsPass.BuildAndValidate();
            Run();
        }

        [MenuItem("Campus Explorer/Interaction/Capture Interactive Light ON OFF")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var outputDirectory = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName,
                "Evidence", "InteractiveLights");
            Directory.CreateDirectory(outputDirectory);

            var switches = UnityEngine.Object.FindObjectsByType<XRLightSwitch>(FindObjectsInactive.Include);
            foreach (var lightSwitch in switches)
                lightSwitch.SendMessage("Awake", SendMessageOptions.RequireReceiver);

            var cameraObject = new GameObject("Interactive Light Evidence Camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 68f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.025f, 0.035f, 1f);
            camera.cullingMask = ~0;
            camera.allowHDR = false;

            var report = "Interactive light render comparison (1024x576)" + Environment.NewLine;
            foreach (var view in Views)
            {
                var lightSwitch = switches.Single(item => item.RoomName == view.room);
                camera.transform.SetPositionAndRotation(view.position,
                    Quaternion.LookRotation(view.target - view.position, Vector3.up));

                var onPixels = Capture(camera, Path.Combine(outputDirectory, view.slug + "-on.png"));
                lightSwitch.Toggle();
                var offPixels = Capture(camera, Path.Combine(outputDirectory, view.slug + "-off.png"));
                lightSwitch.Toggle();

                var difference = MeanRgbDifference(onPixels, offPixels);
                if (difference < 2.0)
                    throw new InvalidOperationException($"{view.room} ON/OFF render difference is too small: {difference:0.000}.");
                report += $"{view.room}: mean RGB byte difference={difference:0.000}; " +
                          $"pointLights={lightSwitch.ControlledLights.Length}; " +
                          $"emissiveTargets={lightSwitch.EmissiveTargets.Length}" + Environment.NewLine;
                Debug.Log($"[Interactive Lights][Capture] {view.room} ON/OFF mean RGB difference: {difference:0.000}.");
            }

            File.WriteAllText(Path.Combine(outputDirectory, "comparison.txt"), report);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[Interactive Lights][Capture] Six ON/OFF evidence images written to " + outputDirectory + ".");
        }

        static Color32[] Capture(Camera camera, string outputPath)
        {
            var renderTexture = new RenderTexture(1024, 576, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            var texture = new Texture2D(1024, 576, TextureFormat.RGB24, false);
            camera.targetTexture = renderTexture;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            texture.ReadPixels(new Rect(0f, 0f, 1024f, 576f), 0, 0);
            texture.Apply();
            var pixels = texture.GetPixels32();
            File.WriteAllBytes(outputPath, texture.EncodeToPNG());
            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(renderTexture);
            return pixels;
        }

        static double MeanRgbDifference(Color32[] first, Color32[] second)
        {
            long total = 0;
            for (var i = 0; i < first.Length; i++)
            {
                total += Math.Abs(first[i].r - second[i].r);
                total += Math.Abs(first[i].g - second[i].g);
                total += Math.Abs(first[i].b - second[i].b);
            }
            return total / (double)(first.Length * 3);
        }
    }
}
