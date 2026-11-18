using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using EcoBot;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Listens for RobotArmController.onGuidanceNeeded and shows:
///   "EcoBot — Confirmation Required"
///   Not sure about 'Toy Car'. Should I pick it up?
///   [ Pick up ]  [ Leave ]
///
/// Pick up -> RobotCleanerAI.ProvideGuidance(target, true)
/// Leave   -> RobotCleanerAI.ProvideGuidance(target, false)
/// Auto-spawns on scene load, persists across scene swaps.
/// </summary>
public class GuidancePromptUI : MonoBehaviour
{
    private static GuidancePromptUI instance;

    private GameObject panel;
    private Text messageText;
    private GameObject currentTarget;
    private RobotArmController currentArm;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (instance != null) return;
        GameObject root = new GameObject("GuidancePromptUI");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<GuidancePromptUI>();
        instance.BuildUI();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        HookIntoScene();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Unhook();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => HookIntoScene();

    private void HookIntoScene()
    {
        Unhook();
        currentArm = FindFirstObjectByType<RobotArmController>();
        if (currentArm != null) currentArm.onGuidanceNeeded.AddListener(OnGuidanceNeeded);
    }

    private void Unhook()
    {
        if (currentArm != null) currentArm.onGuidanceNeeded.RemoveListener(OnGuidanceNeeded);
        currentArm = null;
        Hide();
    }

    private void BuildUI()
    {
        EnsureEventSystem();

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000; // above SceneSwitcherUI (999)

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        gameObject.AddComponent<GraphicRaycaster>();

        // Panel
        panel = new GameObject("GuidancePanel");
        panel.transform.SetParent(transform, false);
        RectTransform pr = panel.AddComponent<RectTransform>();
        pr.anchorMin = new Vector2(0.5f, 0.5f);
        pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        pr.anchoredPosition = Vector2.zero;
        pr.sizeDelta = new Vector2(560f, 220f);

        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.08f, 0.06f, 0.95f);

        // Header
        GameObject header = MakeText(panel.transform, "Header",
            "EcoBot — Confirmation Required", 22, TextAnchor.MiddleCenter, FontStyle.Bold);
        RectTransform hr = header.GetComponent<RectTransform>();
        hr.anchorMin = new Vector2(0f, 1f);
        hr.anchorMax = new Vector2(1f, 1f);
        hr.offsetMin = new Vector2(20f, -52f);
        hr.offsetMax = new Vector2(-20f, -20f);

        // Message
        GameObject msg = MakeText(panel.transform, "Message", "", 18, TextAnchor.MiddleCenter, FontStyle.Normal);
        messageText = msg.GetComponent<Text>();
        RectTransform mr = msg.GetComponent<RectTransform>();
        mr.anchorMin = new Vector2(0f, 0f);
        mr.anchorMax = new Vector2(1f, 1f);
        mr.offsetMin = new Vector2(20f, 90f);
        mr.offsetMax = new Vector2(-20f, -60f);

        // Pick-up button (left half)
        GameObject pickBtn = MakeButton(panel.transform, "Pick up", () => Answer(true));
        RectTransform pbr = pickBtn.GetComponent<RectTransform>();
        pbr.anchorMin = new Vector2(0f, 0f);
        pbr.anchorMax = new Vector2(0.5f, 0f);
        pbr.offsetMin = new Vector2(20f, 20f);
        pbr.offsetMax = new Vector2(-10f, 70f);

        // Leave button (right half)
        GameObject leaveBtn = MakeButton(panel.transform, "Leave", () => Answer(false));
        RectTransform lbr = leaveBtn.GetComponent<RectTransform>();
        lbr.anchorMin = new Vector2(0.5f, 0f);
        lbr.anchorMax = new Vector2(1f, 0f);
        lbr.offsetMin = new Vector2(10f, 20f);
        lbr.offsetMax = new Vector2(-20f, 70f);

        panel.SetActive(false);
    }

    private void OnGuidanceNeeded(GameObject target)
    {
        currentTarget = target;
        if (messageText != null && target != null)
            messageText.text = $"Not sure about '{target.name}'.\nShould I pick it up?";
        if (panel != null) panel.SetActive(true);
    }

    private void Answer(bool approve)
    {
        var target = currentTarget;
        Hide();
        if (target == null) return;

        RobotCleanerAI ai = FindFirstObjectByType<RobotCleanerAI>();
        if (ai != null) ai.ProvideGuidance(target, approve);
        else Debug.LogWarning("[GuidancePromptUI] No RobotCleanerAI in scene to receive ProvideGuidance.");
    }

    private void Hide()
    {
        currentTarget = null;
        if (panel != null) panel.SetActive(false);
    }

    private GameObject MakeText(Transform parent, string name, string content, int size, TextAnchor align, FontStyle style)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        Text t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = Color.white;
        t.text = content;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return go;
    }

    private GameObject MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(label + "Button");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();

        Image img = go.AddComponent<Image>();
        img.color = new Color(0.33f, 0.4f, 0.28f, 1f);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        GameObject txt = MakeText(go.transform, "Label", label, 18, TextAnchor.MiddleCenter, FontStyle.Bold);
        RectTransform tr = txt.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;
        return go;
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        es.AddComponent<InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }
}