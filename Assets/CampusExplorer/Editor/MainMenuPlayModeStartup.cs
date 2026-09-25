using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CampusExplorer.Editor
{
    /// <summary>Keep Play Mode on the scene that is actually open in the Editor.</summary>
    [InitializeOnLoad]
    public static class MainMenuPlayModeStartup
    {
        static MainMenuPlayModeStartup()
        {
            // Clear the previously forced menu startup. Entering Play from a
            // different scene must never silently swap the active XR rig.
            if (!Application.isBatchMode)
                EditorApplication.delayCall += RestoreNormalStartup;
        }

        [MenuItem("Campus Explorer/Main Menu/Restore Normal Play Mode Startup")]
        public static void RestoreNormalStartup()
        {
            EditorSceneManager.playModeStartScene = null;
        }

    }
}
