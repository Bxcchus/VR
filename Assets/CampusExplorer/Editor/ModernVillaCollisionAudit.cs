using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CampusExplorer.Editor
{
    public static class ModernVillaCollisionAudit
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string MarkerPath = "Evidence/modern-villa-collision-audit.request";
        const string OutputPath = "Evidence/modern-villa-collision-pass/renderer-bounds.csv";

        static readonly string[] RelevantTokens =
        {
            "floor", "wall", "door", "stair", "sofa", "table", "island", "cabinet",
            "bed_", "bathtub", "vanity", "television", "tv_", "glass", "pool", "boundary"
        };

        [InitializeOnLoadMethod]
        static void RunRequestedAuditAfterReload()
        {
            if (!File.Exists(MarkerPath))
                return;

            EditorApplication.delayCall += TryRunRequestedAudit;
        }

        static void TryRunRequestedAudit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += TryRunRequestedAudit;
                return;
            }

            try
            {
                AuditRendererBounds();
                File.Delete(MarkerPath);
                Debug.Log("[Modern Villa] Collision renderer-bounds audit completed.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [MenuItem("Campus Explorer/Collision Pass/Audit Renderer Bounds")]
        public static void AuditRendererBounds()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var renderers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => RelevantTokens.Any(token => renderer.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(renderer => renderer.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            var csv = new StringBuilder();
            csv.AppendLine("name,path,active,center_x,center_y,center_z,size_x,size_y,size_z,min_x,min_y,min_z,max_x,max_y,max_z");
            foreach (var renderer in renderers)
            {
                var bounds = renderer.bounds;
                csv.Append(Csv(renderer.name)).Append(',')
                    .Append(Csv(GetPath(renderer.transform))).Append(',')
                    .Append(renderer.gameObject.activeInHierarchy ? "true" : "false").Append(',')
                    .Append(F(bounds.center.x)).Append(',').Append(F(bounds.center.y)).Append(',').Append(F(bounds.center.z)).Append(',')
                    .Append(F(bounds.size.x)).Append(',').Append(F(bounds.size.y)).Append(',').Append(F(bounds.size.z)).Append(',')
                    .Append(F(bounds.min.x)).Append(',').Append(F(bounds.min.y)).Append(',').Append(F(bounds.min.z)).Append(',')
                    .Append(F(bounds.max.x)).Append(',').Append(F(bounds.max.y)).Append(',').Append(F(bounds.max.z)).AppendLine();
            }

            File.WriteAllText(OutputPath, csv.ToString(), new UTF8Encoding(false));
        }

        static string GetPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        static string F(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
