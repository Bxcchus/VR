using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace CampusExplorer
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class PointOfInterest : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] string pointId = "welcome";
        [SerializeField] string title = "Découvrir le campus";
        [SerializeField, TextArea] string body = "Contenu de démonstration : découvrez les espaces et les projets d'un campus fictif.";
        [SerializeField] InfoPanel infoPanel;
        [SerializeField] Renderer targetRenderer;
        [SerializeField] Material idleMaterial;
        [SerializeField] Material hoverMaterial;
        [SerializeField] Button markerButton;
        [SerializeField] Transform markerBillboard;

        XRSimpleInteractable interactable;
        InputAction triggerAction;
        InputAction gripAction;
        readonly HashSet<int> hoveringUiPointers = new();
        SphereCollider hitVolume;
        Camera viewerCamera;

        public string PointId => pointId;
        public string Title => title;
        public string Body => body;
        public InfoPanel Panel => infoPanel;

        public void Configure(string id, string panelTitle, string panelBody, InfoPanel panel,
            Renderer renderer, Material idle, Material hover)
        {
            pointId = id;
            title = panelTitle;
            body = panelBody;
            infoPanel = panel;
            targetRenderer = renderer;
            idleMaterial = idle;
            hoverMaterial = hover;
        }

        public void ConfigureUi(Button button, Transform billboard)
        {
            markerButton = button;
            markerBillboard = billboard;
        }

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            hitVolume = GetComponent<SphereCollider>();
            if (hitVolume != null)
            {
                // The controller rays ignore Trigger colliders in this rig.
                // Keep a solid ray target, then explicitly exclude it from
                // player physics so the marker never blocks navigation.
                hitVolume.isTrigger = false;
                hitVolume.radius = Mathf.Max(hitVolume.radius, 1.4f);
            }
            triggerAction = new InputAction("Open POI", InputActionType.Button);
            triggerAction.AddBinding("<XRController>{LeftHand}/triggerPressed");
            triggerAction.AddBinding("<XRController>{RightHand}/triggerPressed");
            gripAction = new InputAction("Grip POI", InputActionType.Button);
            gripAction.AddBinding("<XRController>{LeftHand}/{GripButton}");
            gripAction.AddBinding("<XRController>{RightHand}/{GripButton}");
            if (markerButton != null)
                markerButton.onClick.AddListener(OpenPanel);
        }

        void Start()
        {
            if (hitVolume == null)
                return;

            foreach (var controller in FindObjectsByType<CharacterController>(FindObjectsInactive.Include))
                Physics.IgnoreCollision(hitVolume, controller, true);
        }

        void OnEnable()
        {
            if (interactable == null)
                interactable = GetComponent<XRSimpleInteractable>();

            interactable.selectEntered.AddListener(OnSelected);
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
            triggerAction.performed += OnTriggerPressed;
            triggerAction.Enable();
            gripAction.performed += OnGripPressed;
            gripAction.Enable();
        }

        void OnDisable()
        {
            if (interactable == null)
                return;

            interactable.selectEntered.RemoveListener(OnSelected);
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
            triggerAction.Disable();
            triggerAction.performed -= OnTriggerPressed;
            gripAction.Disable();
            gripAction.performed -= OnGripPressed;
            hoveringUiPointers.Clear();
        }

        void OnDestroy()
        {
            if (markerButton != null)
                markerButton.onClick.RemoveListener(OpenPanel);
            triggerAction?.Dispose();
            gripAction?.Dispose();
        }

        void LateUpdate()
        {
            if (markerBillboard == null)
                return;
            if (viewerCamera == null || !viewerCamera.isActiveAndEnabled)
                viewerCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (viewerCamera == null)
                return;

            var direction = viewerCamera.transform.position - markerBillboard.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
                markerBillboard.rotation = Quaternion.LookRotation(-direction.normalized, Vector3.up);
        }

        void OnSelected(SelectEnterEventArgs args)
        {
            OpenPanel();
        }

        void OnTriggerPressed(InputAction.CallbackContext context)
        {
            if (hoveringUiPointers.Count > 0 || (interactable != null && interactable.isHovered))
                OpenPanel();
        }

        void OnGripPressed(InputAction.CallbackContext context)
        {
            if (hoveringUiPointers.Count > 0 || (interactable != null && interactable.isHovered))
                OpenPanel();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hoveringUiPointers.Add(eventData.pointerId);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hoveringUiPointers.Remove(eventData.pointerId);
        }

        void OpenPanel()
        {
            TourProgress.Active?.MarkVisited(pointId);
            // A UI click and an XR input can arrive in the same frame. Opening is
            // idempotent; the panel's Close button remains the explicit way out.
            if (infoPanel == null)
                return;
            foreach (var panel in FindObjectsByType<InfoPanel>(FindObjectsInactive.Include))
                if (panel != infoPanel)
                    panel.Close();
            infoPanel.Show(title, body);
        }

        void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (targetRenderer != null && hoverMaterial != null)
                targetRenderer.sharedMaterial = hoverMaterial;
        }

        void OnHoverExited(HoverExitEventArgs args)
        {
            if (targetRenderer != null && idleMaterial != null)
                targetRenderer.sharedMaterial = idleMaterial;
        }
    }
}
