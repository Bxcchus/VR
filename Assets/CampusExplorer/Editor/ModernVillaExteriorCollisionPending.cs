using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace CampusExplorer.Editor
{
    /// <summary>
    /// Applies the exterior-only collision pass once Unity has recompiled. The
    /// marker deliberately avoids the full collision builder, so working indoor
    /// helpers are never recreated by this task.
    /// </summary>
    [InitializeOnLoad]
    static class ModernVillaExteriorCollisionPending
    {
        const string MarkerPath = "Evidence/modern-villa-exterior-collision-pass.request";
        static bool applying;

        static ModernVillaExteriorCollisionPending()
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
                ModernVillaCollisionPass.RebuildExteriorOnly();
                ModernVillaCollisionPass.CaptureDebugViews();
                File.Delete(MarkerPath);
                Debug.Log("[Modern Villa] Exterior-only walkable collision pass applied and captured.");
            }
            catch (Exception exception)
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
