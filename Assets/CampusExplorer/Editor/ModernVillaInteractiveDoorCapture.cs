using System.IO;
using System.Linq;
using CampusExplorer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CampusExplorer.Editor
{
    public static class ModernVillaInteractiveDoorCapture
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";

        [MenuItem("Campus Explorer/Interaction/Capture Interactive Door Closed Open")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var door = Object.FindObjectsByType<XRInteractiveDoor>(FindObjectsInactive.Include)
                .Single(item => item.DoorMesh != null && item.DoorMesh.name == "Door_Interior_08");
            var visual = door.DoorMesh;
            var originalVisualPosition = visual.position;
            var originalVisualRotation = visual.rotation;
            var originalRootRotation = door.transform.rotation;

            var cameraObject = new GameObject("Interactive Door Evidence Camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(1.50f, 66.25f, -25.00f);
            camera.transform.rotation = Quaternion.LookRotation(
                new Vector3(-1.20f, 65.90f, -27.43f) - camera.transform.position, Vector3.up);
            camera.fieldOfView = 62f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 60f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.025f, 0.035f, 1f);
            camera.cullingMask = ~0;
            camera.allowHDR = false;

            var directory = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName,
                "Evidence", "InteractiveDoor");
            Directory.CreateDirectory(directory);
            Capture(camera, Path.Combine(directory, "door-closed.png"));
            var opening = Quaternion.AngleAxis(door.OpenAngle, Vector3.up);
            door.transform.rotation = opening * originalRootRotation;
            visual.SetPositionAndRotation(
                door.transform.position + opening * (originalVisualPosition - door.transform.position),
                opening * originalVisualRotation);
            Capture(camera, Path.Combine(directory, "door-open.png"));

            visual.SetPositionAndRotation(originalVisualPosition, originalVisualRotation);
            Object.DestroyImmediate(cameraObject);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[Interactive Door][Capture] Closed/open evidence written to " + directory + ".");
        }

        static void Capture(Camera camera, string path)
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
            File.WriteAllBytes(path, texture.EncodeToPNG());
            RenderTexture.active = previous;
            camera.targetTexture = null;
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(renderTexture);
        }
    }
}
