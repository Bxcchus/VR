using UnityEngine;
using UnityEngine.UI;

namespace CampusExplorer
{
    public sealed class InfoPanel : MonoBehaviour
    {
        [SerializeField] GameObject panelRoot;
        [SerializeField] Text titleText;
        [SerializeField] Text bodyText;
        [SerializeField] Button closeButton;

        Camera viewerCamera;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;
        public GameObject PanelRoot => panelRoot;
        public Text TitleText => titleText;
        public Text BodyText => bodyText;
        public Button CloseButton => closeButton;

        void Awake()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
        }

        public void Configure(GameObject root, Text title, Text body, Button close)
        {
            panelRoot = root;
            titleText = title;
            bodyText = body;
            closeButton = close;
        }

        public void Toggle(string title, string body)
        {
            if (IsOpen)
            {
                Close();
                return;
            }

            foreach (var panel in FindObjectsByType<InfoPanel>(FindObjectsInactive.Include))
            {
                if (panel != this)
                    panel.Close();
            }
            Show(title, body);
        }

        public void Show(string title, string body)
        {
            if (titleText != null)
                titleText.text = title;
            if (bodyText != null)
                bodyText.text = body;
            if (panelRoot != null)
                panelRoot.SetActive(true);
            FaceViewer();
        }

        public void Close()
        {
            if (panelRoot != null)
                panelRoot.SetActive(false);
        }

        void LateUpdate()
        {
            if (IsOpen)
                FaceViewer();
        }

        void FaceViewer()
        {
            if (panelRoot == null)
                return;
            if (viewerCamera == null || !viewerCamera.isActiveAndEnabled)
                viewerCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (viewerCamera == null)
                return;

            var direction = viewerCamera.transform.position - panelRoot.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
                panelRoot.transform.rotation = Quaternion.LookRotation(-direction.normalized, Vector3.up);
        }
    }
}
