using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CampusExplorer
{
    public sealed class TourProgress : MonoBehaviour
    {
        [SerializeField] int requiredPoints = 4;
        [SerializeField] Text progressText;
        [SerializeField] GameObject completionPanel;
        [SerializeField] Button restartButton;

        readonly HashSet<string> visited = new HashSet<string>();

        public static TourProgress Active { get; private set; }
        public int VisitedCount => visited.Count;
        public int RequiredPoints => requiredPoints;
        public bool IsComplete => requiredPoints > 0 && visited.Count >= requiredPoints;

        public void Configure(int pointCount, Text label, GameObject completion, Button restart)
        {
            requiredPoints = pointCount;
            progressText = label;
            completionPanel = completion;
            restartButton = restart;
            Refresh();
        }

        void Awake()
        {
            Active = this;
            if (restartButton != null)
                restartButton.onClick.AddListener(RestartTour);
            Refresh();
        }

        void OnDestroy()
        {
            if (Active == this)
                Active = null;
            if (restartButton != null)
                restartButton.onClick.RemoveListener(RestartTour);
        }

        public bool MarkVisited(string pointId)
        {
            if (string.IsNullOrWhiteSpace(pointId) || !visited.Add(pointId))
                return false;

            Refresh();
            return true;
        }

        public void RestartTour()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void Refresh()
        {
            if (progressText != null)
                progressText.text = $"PARCOURS  {visited.Count}/{requiredPoints}";
            if (completionPanel != null)
                completionPanel.SetActive(IsComplete);
        }
    }
}
