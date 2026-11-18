using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Spawns a persistent UI button in the top-right corner that toggles between
/// HighFidelityScene and SampleScene.
/// </summary>
public class SceneSwitcherUI : MonoBehaviour
{
    private const string HighFidelitySceneName = "HighFidelityScene";
    private const string SampleSceneName = "SampleScene";

    private static SceneSwitcherUI instance;
    private Text buttonLabel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (instance != null)
        {
            return;
        }

        GameObject root = new GameObject("SceneSwitcherUI");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<SceneSwitcherUI>();
        instance.BuildUI();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
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

        GameObject buttonObj = new GameObject("SwitchSceneButton");
        buttonObj.transform.SetParent(transform, false);

        RectTransform rect = buttonObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-20f, -20f);
        rect.sizeDelta = new Vector2(240f, 48f);

        Image bg = buttonObj.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.14f, 0.18f, 0.9f);

        Button button = buttonObj.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(ToggleScene);

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

        UpdateLabel(SceneManager.GetActiveScene().name);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureEventSystem();
        UpdateLabel(scene.name);
    }

    private void UpdateLabel(string currentSceneName)
    {
        if (buttonLabel == null)
        {
            return;
        }

        string nextScene = currentSceneName == HighFidelitySceneName
            ? SampleSceneName
            : HighFidelitySceneName;
        buttonLabel.text = $"Switch to {nextScene}";
    }

    private void ToggleScene()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        string targetScene = currentScene == HighFidelitySceneName
            ? SampleSceneName
            : HighFidelitySceneName;
        SceneManager.LoadScene(targetScene);
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObj = new GameObject("EventSystem");
        eventSystemObj.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        eventSystemObj.AddComponent<InputSystemUIInputModule>();
#else
        eventSystemObj.AddComponent<StandaloneInputModule>();
#endif
    }
}
