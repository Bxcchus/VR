using UnityEngine;
using UnityEngine.UI;

namespace CampusExplorer
{
    public sealed class XRGuidedTour : MonoBehaviour
    {
        [SerializeField] XRRoomMenu roomMenu;
        [SerializeField] XRRoomTeleporter roomTeleporter;
        [SerializeField] GameObject panelRoot;
        [SerializeField] Transform panelTransform;
        [SerializeField] GameObject tourContent;
        [SerializeField] GameObject completionContent;
        [SerializeField] Text stepText;
        [SerializeField] Text roomTitle;
        [SerializeField] Text descriptionText;
        [SerializeField] Button previousButton;
        [SerializeField] Button nextButton;
        [SerializeField] Text nextButtonLabel;
        [SerializeField] Button exitButton;
        [SerializeField] Button freeExplorationButton;
        [SerializeField] Button restartButton;

        static readonly string[] TourRooms =
        {
            "Kitchen",
            "Living Room",
            "Dining Room",
            "Master Bedroom",
            "Bathroom",
            "Pool / Terrace",
        };

        static readonly string[] TourDescriptions =
        {
            "Modern open-plan kitchen centered around a large island and connected to the main living spaces.",
            "Central lounge area designed around large openings and direct access to the surrounding living spaces.",
            "Dining space positioned between the kitchen and main living area for an open and connected layout.",
            "Private upper-floor bedroom with a spacious sleeping area and direct access to the surrounding private spaces.",
            "Modern bathroom combining dark finishes, glass surfaces and dedicated washing and shower areas.",
            "Outdoor relaxation area extending the living space toward the terrace and swimming pool.",
        };

        Camera viewerCamera;
        int currentStep;
        int pendingStep = -1;

        public bool IsTourActive { get; private set; }
        public bool IsComplete { get; private set; }
        public int CurrentStep => currentStep;
        public int StepCount => TourRooms.Length;
        public string CurrentRoom => TourRooms[Mathf.Clamp(currentStep, 0, TourRooms.Length - 1)];
        public GameObject PanelRoot => panelRoot;
        public GameObject TourContent => tourContent;
        public GameObject CompletionContent => completionContent;
        public Button PreviousButton => previousButton;
        public Button NextButton => nextButton;
        public Button ExitButton => exitButton;
        public Button FreeExplorationButton => freeExplorationButton;
        public Button RestartButton => restartButton;
        public string[] RoomSequence => (string[])TourRooms.Clone();
        public float LastPlacementClearance { get; private set; }

        public void Configure(XRRoomMenu menu, XRRoomTeleporter teleporter, GameObject panel,
            Transform placementTransform, GameObject activeTourContent, GameObject completedTourContent,
            Text step, Text title, Text description, Button previous, Button next, Text nextLabel,
            Button exit, Button freeExploration, Button restart)
        {
            roomMenu = menu;
            roomTeleporter = teleporter;
            panelRoot = panel;
            panelTransform = placementTransform;
            tourContent = activeTourContent;
            completionContent = completedTourContent;
            stepText = step;
            roomTitle = title;
            descriptionText = description;
            previousButton = previous;
            nextButton = next;
            nextButtonLabel = nextLabel;
            exitButton = exit;
            freeExplorationButton = freeExploration;
            restartButton = restart;
        }

        void Awake()
        {
            previousButton?.onClick.AddListener(Previous);
            nextButton?.onClick.AddListener(Next);
            exitButton?.onClick.AddListener(ExitTour);
            freeExplorationButton?.onClick.AddListener(ExitTour);
            restartButton?.onClick.AddListener(RestartTour);
            if (panelRoot != null)
                panelRoot.SetActive(false);
        }

        void OnEnable()
        {
            if (roomMenu != null)
                roomMenu.GuidedTourRequested += StartTour;
            if (roomTeleporter != null)
                roomTeleporter.RoomTeleportCompleted += OnRoomTeleportCompleted;
        }

        void OnDisable()
        {
            if (roomMenu != null)
                roomMenu.GuidedTourRequested -= StartTour;
            if (roomTeleporter != null)
                roomTeleporter.RoomTeleportCompleted -= OnRoomTeleportCompleted;
        }

        void OnDestroy()
        {
            previousButton?.onClick.RemoveListener(Previous);
            nextButton?.onClick.RemoveListener(Next);
            exitButton?.onClick.RemoveListener(ExitTour);
            freeExplorationButton?.onClick.RemoveListener(ExitTour);
            restartButton?.onClick.RemoveListener(RestartTour);
        }

        public void StartTour()
        {
            if (roomTeleporter == null || roomTeleporter.IsTeleporting)
                return;
            roomMenu?.Close();
            IsTourActive = true;
            IsComplete = false;
            RequestStep(0);
        }

        public void Previous()
        {
            if (!IsTourActive || IsComplete || currentStep <= 0)
                return;
            RequestStep(currentStep - 1);
        }

        public void Next()
        {
            if (!IsTourActive || IsComplete)
                return;
            if (currentStep >= TourRooms.Length - 1)
            {
                ShowCompletion();
                return;
            }
            RequestStep(currentStep + 1);
        }

        public void ExitTour()
        {
            pendingStep = -1;
            IsTourActive = false;
            IsComplete = false;
            if (panelRoot != null)
                panelRoot.SetActive(false);
        }

        public void RestartTour()
        {
            if (roomTeleporter == null || roomTeleporter.IsTeleporting)
                return;
            IsTourActive = true;
            IsComplete = false;
            RequestStep(0);
        }

        void RequestStep(int step)
        {
            if (roomTeleporter == null || roomTeleporter.IsTeleporting)
                return;
            pendingStep = Mathf.Clamp(step, 0, TourRooms.Length - 1);
            if (panelRoot != null)
                panelRoot.SetActive(false);
            if (!roomTeleporter.TeleportToRoom(TourRooms[pendingStep]))
            {
                Debug.LogWarning($"[Guided Tour] Could not teleport to {TourRooms[pendingStep]}.");
                pendingStep = -1;
            }
        }

        void OnRoomTeleportCompleted(string roomName)
        {
            if (!IsTourActive || pendingStep < 0 || roomName != TourRooms[pendingStep])
                return;
            currentStep = pendingStep;
            pendingStep = -1;
            UpdateTourPanel();
            PlacePanelInFreeView();
            panelRoot?.SetActive(true);
            Debug.Log($"[Guided Tour] Step {currentStep + 1}/{TourRooms.Length}: {CurrentRoom}.");
            if (CurrentRoom == "Bathroom")
                LogBathroomState();
        }

        void UpdateTourPanel()
        {
            IsComplete = false;
            tourContent?.SetActive(true);
            completionContent?.SetActive(false);
            if (stepText != null)
                stepText.text = $"Step {currentStep + 1} / {TourRooms.Length}";
            if (roomTitle != null)
                roomTitle.text = TourRooms[currentStep].ToUpperInvariant();
            if (descriptionText != null)
                descriptionText.text = TourDescriptions[currentStep];
            if (previousButton != null)
                previousButton.interactable = currentStep > 0;
            if (nextButtonLabel != null)
                nextButtonLabel.text = currentStep == TourRooms.Length - 1 ? "FINISH" : "NEXT";
        }

        void ShowCompletion()
        {
            IsComplete = true;
            tourContent?.SetActive(false);
            completionContent?.SetActive(true);
            PlacePanelInFreeView();
            panelRoot?.SetActive(true);
            Debug.Log("[Guided Tour] Tour complete.");
        }

        void PlacePanelInFreeView()
        {
            if (panelTransform == null)
                return;
            if (viewerCamera == null || !viewerCamera.isActiveAndEnabled)
                viewerCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (viewerCamera == null)
                return;

            var forward = Vector3.ProjectOnPlane(viewerCamera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var origin = viewerCamera.transform.position;
            if (CurrentRoom == "Bathroom" && !IsComplete)
            {
                // The validated Bathroom anchor faces shower glass only 0.27 m away.
                // Keep the anchor and teleport untouched, and place this panel in the
                // nearest clear direction that still remains inside the headset's view.
                var bathroomDirection = (forward * 0.258819f - right * 0.9659258f).normalized;
                const float bathroomDistance = 0.80f;
                LastPlacementClearance = MeasureClearance(origin, bathroomDirection, 0.40f, 1.35f);
                panelTransform.position = origin + bathroomDirection * bathroomDistance + Vector3.down * 0.05f;
                panelTransform.rotation = Quaternion.LookRotation(bathroomDirection, Vector3.up);
                return;
            }
            var candidates = new[] { forward, right, -right, -forward };
            var selectedDirection = forward;
            var bestClearance = 0f;

            foreach (var direction in candidates)
            {
                var clearance = 1.35f;
                if (Physics.SphereCast(origin, 0.30f, direction, out var hit, clearance,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    clearance = hit.distance;
                if (direction == forward && clearance >= 1.0f)
                {
                    selectedDirection = direction;
                    bestClearance = clearance;
                    break;
                }
                if (clearance > bestClearance)
                {
                    selectedDirection = direction;
                    bestClearance = clearance;
                }
            }

            LastPlacementClearance = bestClearance;
            var distance = Mathf.Clamp(bestClearance - 0.20f, 0.60f, 1.15f);
            panelTransform.position = origin + selectedDirection * distance + Vector3.down * 0.05f;
            panelTransform.rotation = Quaternion.LookRotation(selectedDirection, Vector3.up);
        }

        static float MeasureClearance(Vector3 origin, Vector3 direction, float radius, float maximumDistance)
        {
            return Physics.SphereCast(origin, radius, direction, out var hit, maximumDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.distance
                : maximumDistance;
        }

        void LogBathroomState()
        {
            Debug.Log("[Guided Tour][Bathroom Debug]\n" +
                      $"Current step index: {currentStep}\n" +
                      $"Current room name: {CurrentRoom}\n" +
                      $"Title text: {(roomTitle != null ? roomTitle.text : "<null>")}\n" +
                      $"Description text: {(descriptionText != null ? descriptionText.text : "<null>")}\n" +
                      $"TourContent active: {(tourContent != null && tourContent.activeSelf)}\n" +
                      $"CompletionContent active: {(completionContent != null && completionContent.activeSelf)}\n" +
                      $"Panel active: {(panelRoot != null && panelRoot.activeSelf)}\n" +
                      $"Panel world position: {(panelTransform != null ? panelTransform.position.ToString("F3") : "<null>")}\n" +
                      $"Panel forward direction: {(panelTransform != null ? panelTransform.forward.ToString("F3") : "<null>")}\n" +
                      $"Measured clearance: {LastPlacementClearance:F3} m");
        }
    }
}
