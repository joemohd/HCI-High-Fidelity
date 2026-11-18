using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using EcoBot;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Spawns a persistent UI button in the top-right corner (below SceneSwitcherUI)
/// that toggles between the Main Camera and the Camera inside the robot.
/// </summary>
public class CameraSwitcherUI : MonoBehaviour
{
    private static CameraSwitcherUI instance;

    private GameObject buttonObj;
    private Text buttonLabel;
    private Camera mainCamera;
    private Camera robotCamera;
    private bool usingRobotCamera;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (instance != null) return;
        GameObject root = new GameObject("CameraSwitcherUI");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<CameraSwitcherUI>();
        instance.BuildUI();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        RefreshCameras();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureEventSystem();
        RefreshCameras();
    }

    private void BuildUI()
    {
        EnsureEventSystem();

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        gameObject.AddComponent<GraphicRaycaster>();

        buttonObj = new GameObject("SwitchCameraButton");
        buttonObj.transform.SetParent(transform, false);

        RectTransform rect = buttonObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-20f, -78f); // right below SceneSwitcherUI
        rect.sizeDelta = new Vector2(240f, 48f);

        Image bg = buttonObj.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.14f, 0.18f, 0.9f);

        Button button = buttonObj.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(ToggleCamera);

        GameObject textObj = new GameObject("Label");
        textObj.transform.SetParent(buttonObj.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        buttonLabel = textObj.AddComponent<Text>();
        buttonLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        buttonLabel.fontSize = 18;
        buttonLabel.alignment = TextAnchor.MiddleCenter;
        buttonLabel.color = Color.white;

        RefreshCameras();
    }

    private void RefreshCameras()
    {
        mainCamera = null;
        robotCamera = null;
        usingRobotCamera = false;

        RobotCleanerAI robot = FindAnyObjectByType<RobotCleanerAI>();
        if (robot != null)
        {
            robotCamera = robot.GetComponentInChildren<Camera>(true);
        }

        Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Camera cam in allCameras)
        {
            if (cam == null) continue;

            if (robotCamera == null && cam != Camera.main &&
                (cam.name.ToLowerInvariant().Contains("robot") || cam.transform.root.name.ToLowerInvariant().Contains("robot")))
            {
                robotCamera = cam;
                continue;
            }

            if (cam != robotCamera && (cam.CompareTag("MainCamera") || mainCamera == null))
            {
                mainCamera = cam;
            }
        }

        if (buttonObj != null)
        {
            buttonObj.SetActive(mainCamera != null && robotCamera != null);
        }

        if (mainCamera != null && robotCamera != null)
        {
            ApplyCameraState(useRobot: false);
        }
    }

    private void ToggleCamera()
    {
        if (mainCamera == null || robotCamera == null)
        {
            RefreshCameras();
            if (mainCamera == null || robotCamera == null) return;
        }

        ApplyCameraState(!usingRobotCamera);
    }

    private void ApplyCameraState(bool useRobot)
    {
        usingRobotCamera = useRobot;

        if (mainCamera != null)
        {
            if (!mainCamera.gameObject.activeSelf) mainCamera.gameObject.SetActive(true);
            mainCamera.enabled = !useRobot;

            AudioListener mainListener = mainCamera.GetComponent<AudioListener>();
            if (mainListener != null) mainListener.enabled = !useRobot;

            CameraFlyController fly = mainCamera.GetComponent<CameraFlyController>();
            if (fly != null) fly.enabled = !useRobot;
        }

        if (robotCamera != null)
        {
            if (!robotCamera.gameObject.activeSelf) robotCamera.gameObject.SetActive(true);
            robotCamera.enabled = useRobot;

            AudioListener robotListener = robotCamera.GetComponent<AudioListener>();
            if (robotListener != null) robotListener.enabled = useRobot;
        }

        if (buttonLabel != null)
        {
            buttonLabel.text = useRobot ? "Switch to Main Cam" : "Switch to Robot Cam";
        }
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;

        GameObject eventSystemObj = new GameObject("EventSystem");
        eventSystemObj.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        eventSystemObj.AddComponent<InputSystemUIInputModule>();
#else
        eventSystemObj.AddComponent<StandaloneInputModule>();
#endif
    }
}
