using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CampusExplorer
{
    public sealed class XRRoomMenu : MonoBehaviour
    {
        [SerializeField] GameObject menuRoot;
        [SerializeField] Transform menuTransform;
        [SerializeField] Text selectionText;
        [SerializeField] Button closeButton;
        [SerializeField] Button guidedTourButton;
        [SerializeField] Button[] roomButtons;
        [SerializeField] string[] roomNames;

        InputAction toggleAction;
        Camera viewerCamera;

        public event Action<string> RoomSelected;
        public event Action GuidedTourRequested;

        public bool IsOpen => menuRoot != null && menuRoot.activeSelf;
        public GameObject MenuRoot => menuRoot;
        public Button CloseButton => closeButton;
        public Button GuidedTourButton => guidedTourButton;
        public Button[] RoomButtons => roomButtons;
        public string[] RoomNames => roomNames;
        public string LastSelectedRoom { get; private set; }
        public float LastPlacementClearance { get; private set; }

        public void Configure(GameObject root, Transform placementTransform, Text status,
            Button close, Button[] buttons, string[] names)
        {
            menuRoot = root;
            menuTransform = placementTransform;
            selectionText = status;
            closeButton = close;
            roomButtons = buttons;
            roomNames = names;
        }

        public void ConfigureGuidedTourButton(Button button)
        {
            guidedTourButton = button;
        }

        void Awake()
        {
            toggleAction = new InputAction("Toggle XR Room Menu", InputActionType.Button);
            toggleAction.AddBinding("<XRController>{LeftHand}/menuButton");
            toggleAction.AddBinding("<Keyboard>/m");

            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
            if (guidedTourButton != null)
                guidedTourButton.onClick.AddListener(StartGuidedTour);

            if (roomButtons == null || roomNames == null)
                return;
            var count = Mathf.Min(roomButtons.Length, roomNames.Length);
            for (var index = 0; index < count; index++)
            {
                var capturedRoom = roomNames[index];
                roomButtons[index].onClick.AddListener(() => SelectRoom(capturedRoom));
            }
        }

        void OnEnable()
        {
            toggleAction.performed += OnTogglePerformed;
            toggleAction.Enable();
        }

        void OnDisable()
        {
            toggleAction.Disable();
            toggleAction.performed -= OnTogglePerformed;
        }

        void OnDestroy()
        {
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);
            if (guidedTourButton != null)
                guidedTourButton.onClick.RemoveListener(StartGuidedTour);
        }

        void OnTogglePerformed(InputAction.CallbackContext context)
        {
            Toggle();
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        public void Open()
        {
            if (menuRoot == null || menuTransform == null)
                return;

            PlaceInFreeView();
            menuRoot.SetActive(true);
        }

        public void Close()
        {
            if (menuRoot != null)
                menuRoot.SetActive(false);
        }

        public void SelectRoom(string roomName)
        {
            LastSelectedRoom = roomName;
            if (selectionText != null)
                selectionText.text = $"Selected: {roomName}";
            Debug.Log($"[XR Room Menu] Selected room: {roomName}");
            RoomSelected?.Invoke(roomName);
        }

        void StartGuidedTour()
        {
            Close();
            GuidedTourRequested?.Invoke();
        }

        void PlaceInFreeView()
        {
            if (viewerCamera == null || !viewerCamera.isActiveAndEnabled)
                viewerCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (viewerCamera == null)
                return;

            var forward = Vector3.ProjectOnPlane(viewerCamera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var candidates = new[] { forward, right, -right, -forward };
            var origin = viewerCamera.transform.position;
            var selectedDirection = forward;
            var bestClearance = 0f;

            foreach (var direction in candidates)
            {
                var clearance = 1.25f;
                if (Physics.SphereCast(origin, 0.28f, direction, out var hit, clearance, Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore))
                    clearance = hit.distance;
                if (direction == forward && clearance >= 0.9f)
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
            var distance = Mathf.Clamp(bestClearance - 0.18f, 0.50f, 1.10f);
            menuTransform.position = origin + selectedDirection * distance + Vector3.down * 0.10f;
            menuTransform.rotation = Quaternion.LookRotation(selectedDirection, Vector3.up);
        }
    }
}
