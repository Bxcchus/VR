using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace CampusExplorer.Editor
{
    /// <summary>
    /// Applies a pending collision rebuild after Play Mode has stopped and scripts
    /// have reloaded. The marker makes this a one-shot operation.
    /// </summary>
    [InitializeOnLoad]
    static class ModernVillaCollisionPendingHotfix
    {
        const string MarkerPath = "Temp/modern-villa-collision-hotfix.pending";
        static bool applying;

        static ModernVillaCollisionPendingHotfix()
        {
            EditorApplication.delayCall += TryApply;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [DidReloadScripts]
        static void OnScriptsReloaded() => EditorApplication.delayCall += TryApply;

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += TryApply;
        }

        static void TryApply()
        {
            if (applying || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(MarkerPath))
                return;

            applying = true;
            try
            {
                ModernVillaCollisionPass.BuildAndValidate();
                File.Delete(MarkerPath);
                Debug.Log("[Modern Villa] Pending stair-edge collision hotfix applied.");
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                applying = false;
            }
        }
    }
}
