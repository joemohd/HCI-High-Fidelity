using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using EcoBot;

/// <summary>
/// Listens for RobotCleanerAI.onAvoidableEncountered and shows a non-blocking
/// error/warning banner styled like GuidancePromptUI that fades in, stays for
/// 5 seconds, and fades out.
/// Auto-spawns on scene load and persists across scene swaps.
/// </summary>
public class AvoidableAlertUI : MonoBehaviour
{
    private const float FadeInDuration = 0.3f;
    private const float HoldDuration = 5.0f;
    private const float FadeOutDuration = 0.4f;

    private static AvoidableAlertUI instance;

    private GameObject panel;
    private CanvasGroup canvasGroup;
    private Text messageText;
    private RobotCleanerAI currentAI;
    private Coroutine displayRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (instance != null) return;
        GameObject root = new GameObject("AvoidableAlertUI");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<AvoidableAlertUI>();
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
        currentAI = FindAnyObjectByType<RobotCleanerAI>();
        if (currentAI != null) currentAI.onAvoidableEncountered.AddListener(OnAvoidableEncountered);
    }

    private void Unhook()
    {
        if (currentAI != null) currentAI.onAvoidableEncountered.RemoveListener(OnAvoidableEncountered);
        currentAI = null;
        HideImmediate();
    }

    private void BuildUI()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1001; // above GuidancePromptUI (1000)

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // Panel (top-center so it stays readable and doesn't cover the robot)
        panel = new GameObject("AvoidableAlertPanel");
        panel.transform.SetParent(transform, false);
        RectTransform pr = panel.AddComponent<RectTransform>();
        pr.anchorMin = new Vector2(0.5f, 1f);
        pr.anchorMax = new Vector2(0.5f, 1f);
        pr.pivot = new Vector2(0.5f, 1f);
        pr.anchoredPosition = new Vector2(0f, -36f);
        pr.sizeDelta = new Vector2(560f, 128f);

        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0.14f, 0.06f, 0.06f, 0.94f);
        bg.raycastTarget = false;

        canvasGroup = panel.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        // Header
        GameObject header = MakeText(panel.transform, "Header",
            "EcoBot — Avoidable Hazard Detected", 21, TextAnchor.MiddleCenter, FontStyle.Bold,
            new Color(1f, 0.45f, 0.42f, 1f));
        RectTransform hr = header.GetComponent<RectTransform>();
        hr.anchorMin = new Vector2(0f, 1f);
        hr.anchorMax = new Vector2(1f, 1f);
        hr.offsetMin = new Vector2(20f, -46f);
        hr.offsetMax = new Vector2(-20f, -14f);

        // Message
        GameObject msg = MakeText(panel.transform, "Message", "", 18, TextAnchor.MiddleCenter, FontStyle.Normal, Color.white);
        messageText = msg.GetComponent<Text>();
        RectTransform mr = msg.GetComponent<RectTransform>();
        mr.anchorMin = new Vector2(0f, 0f);
        mr.anchorMax = new Vector2(1f, 1f);
        mr.offsetMin = new Vector2(20f, 14f);
        mr.offsetMax = new Vector2(-20f, -48f);

        panel.SetActive(false);
    }

    private void OnAvoidableEncountered(GameObject avoidable)
    {
        if (panel == null || canvasGroup == null) return;

        string itemName = avoidable != null ? avoidable.name : "Hazard";
        if (messageText != null)
        {
            messageText.text = $"Encountered '{itemName}' on the floor.\nTurning around to continue my task.";
        }

        if (displayRoutine != null) StopCoroutine(displayRoutine);
        displayRoutine = StartCoroutine(FadeRoutine());
    }

    private IEnumerator FadeRoutine()
    {
        panel.SetActive(true);

        float startAlpha = canvasGroup.alpha;
        float t = 0f;
        while (t < FadeInDuration)
        {
            t += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, Mathf.Clamp01(t / FadeInDuration));
            yield return null;
        }
        canvasGroup.alpha = 1f;

        yield return new WaitForSecondsRealtime(HoldDuration);

        t = 0f;
        while (t < FadeOutDuration)
        {
            t += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(t / FadeOutDuration));
            yield return null;
        }

        HideImmediate();
    }

    private void HideImmediate()
    {
        if (displayRoutine != null)
        {
            StopCoroutine(displayRoutine);
            displayRoutine = null;
        }
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (panel != null) panel.SetActive(false);
    }

    private GameObject MakeText(Transform parent, string name, string content, int size, TextAnchor align, FontStyle style, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        Text t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = content;
        t.fontSize = size;
        t.alignment = align;
        t.fontStyle = style;
        t.color = color;
        t.raycastTarget = false;
        return go;
    }
}
