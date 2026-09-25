using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CampusExplorer.Editor
{
    /// <summary>
    /// One-shot final evidence runner. A request marker lets an audit requested
    /// during Play Mode continue safely after the editor returns to Edit Mode and
    /// recompiles. It never rebuilds or edits the physical collider layout.
    /// </summary>
    public static class ModernVillaFinalCollisionAuditRunner
    {
        const string RequestPath = "Evidence/modern-villa-final-collision-audit.request";
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string EvidenceFolder = "Evidence/modern-villa-final-collision-audit";
        const string ValidationPath = EvidenceFolder + "/editor-validation.txt";
        static double notBefore;
        static bool running;

        [InitializeOnLoadMethod]
        static void QueueRequestedAudit()
        {
            if (!File.Exists(RequestPath))
                return;

            notBefore = EditorApplication.timeSinceStartup + 2d;
            EditorApplication.update -= RunWhenReady;
            EditorApplication.update += RunWhenReady;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.isPlaying = false;
        }

        static void RunWhenReady()
        {
            if (running || !File.Exists(RequestPath)
                || Application.isPlaying || EditorApplication.isPlaying
                || EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.timeSinceStartup < notBefore)
                return;

            running = true;
            EditorApplication.update -= RunWhenReady;
            Directory.CreateDirectory(EvidenceFolder);
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var collisions = GameObject.Find("Collisions");
                var interactiveDoors = GameObject.Find("InteractiveDoors");
                Require(collisions != null, "Collisions hierarchy is missing.");
                Require(interactiveDoors != null, "InteractiveDoors hierarchy is missing.");

                var environment = collisions.GetComponentsInChildren<BoxCollider>(true);
                var doors = interactiveDoors.GetComponentsInChildren<BoxCollider>(true);
                var all = environment.Concat(doors).Distinct().ToArray();
                Require(environment.Length == 151, $"Expected 151 environment colliders, found {environment.Length}.");
                Require(doors.Length == 16, $"Expected 16 moving door colliders, found {doors.Length}.");
                Require(all.Length == 167, $"Expected 167 total colliders, found {all.Length}.");
                Require(all.All(collider => collider.enabled), "A collider component is disabled.");
                Require(all.All(collider => collider.gameObject.activeInHierarchy), "A collider GameObject is inactive.");
                Require(all.All(collider => !collider.isTrigger), "An audited navigation collider is a trigger.");
                Require(all.All(collider => collider.gameObject.layer == 0), "An audited collider is not on Default layer 0.");
                Require(!Physics.GetIgnoreLayerCollision(0, 0), "Default-to-Default collision is disabled.");

                var debug = collisions.GetComponent<CollisionDebugVisualizer>();
                Require(debug != null, "CollisionDebugVisualizer is missing.");
                debug.SetVisible(true);
                var debugCount = collisions.GetComponentsInChildren<Renderer>(true)
                    .Count(renderer => renderer.name.StartsWith("Debug - ", StringComparison.Ordinal));
                Require(debugCount == 167, $"Expected 167 F8 visuals, found {debugCount}.");
                debug.SetVisible(false);

                ModernVillaCollisionPass.CaptureDebugViews();

                var validation = new StringBuilder();
                validation.AppendLine("FINAL COLLISION EDITOR VALIDATION: PASS");
                validation.AppendLine($"Generated={DateTime.Now:O}");
                validation.AppendLine($"Scene={scene.path}");
                validation.AppendLine($"EnvironmentBoxColliders={environment.Length}");
                validation.AppendLine($"InteractiveDoorBoxColliders={doors.Length}");
                validation.AppendLine($"TotalBoxColliders={all.Length}");
                validation.AppendLine($"F8DebugVisuals={debugCount}");
                validation.AppendLine("AllEnabled=True");
                validation.AppendLine("AllActive=True");
                validation.AppendLine("AllSolid=True");
                validation.AppendLine("AllLayer0=True");
                validation.AppendLine("DefaultToDefaultCollision=True");
                File.WriteAllText(ValidationPath, validation.ToString(), new UTF8Encoding(false));

                Quest3BuildTools.BuildApk();
                var apk = new FileInfo("Builds/Quest3/CampusExplorerXR-Quest3.apk");
                Require(apk.Exists && apk.Length > 0, "Quest 3 APK was not produced.");
                File.AppendAllLines(ValidationPath, new[]
                {
                    "Quest3Build=PASS",
                    $"Quest3ApkBytes={apk.Length}",
                    $"Quest3ApkModified={apk.LastWriteTime:O}"
                });
                File.Delete(RequestPath);
                AssetDatabase.Refresh();
                Debug.Log("[Modern Villa] FINAL COLLISION AUDIT PASS. F8 evidence and Quest 3 APK are current.");
            }
            catch (Exception exception)
            {
                File.WriteAllText(ValidationPath,
                    "FINAL COLLISION EDITOR VALIDATION: FAIL\n" + exception,
                    new UTF8Encoding(false));
                File.Delete(RequestPath);
                Debug.LogException(exception);
            }
            finally
            {
                running = false;
            }
        }

        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
