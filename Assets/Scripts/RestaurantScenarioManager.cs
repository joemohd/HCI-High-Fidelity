using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RestaurantScenarioManager : MonoBehaviour
{
    public static RestaurantScenarioManager Instance { get; private set; }

    [Header("Actors & Scene References")]
    public EcoBotController ecobot;
    public Transform hostCharacter;
    public List<Transform> guestCharacters = new List<Transform>();
    public Transform roomEntryWaypoint;
    public Transform exitDoorWaypoint;
    public List<CleanableItem> itemsToClean = new List<CleanableItem>();

    [Header("Scenario Progress")]
    public string currentPhase = "Guests Finishing Meal";
    public bool isSimulationRunning = false;

    // Guidance dialog callback
    private Action onGuidanceResolved;
    private CleanableItem pendingGuidanceItem;
    private bool isAwaitingGuidance = false;

    void Awake()
    {
        Instance = this;
    }

    public void StartFullScenario()
    {
        if (isSimulationRunning) return;
        StartCoroutine(ScenarioSequence());
    }

    private IEnumerator ScenarioSequence()
    {
        isSimulationRunning = true;
        currentPhase = "1. Guests finish meal and prepare to leave";

        // Wait a few seconds with guests at the table
        yield return new WaitForSeconds(3.0f);

        // Guests stand up and exit restaurant
        currentPhase = "2. Guests leave the restaurant";
        foreach (var guest in guestCharacters)
        {
            if (guest != null && exitDoorWaypoint != null)
            {
                StartCoroutine(MoveActor(guest, exitDoorWaypoint.position, 2.5f));
            }
        }
        yield return new WaitForSeconds(3.0f);

        // Host observes table is messy and calls EcoBot
        currentPhase = "3. Host summons EcoBot via tablet/voice command";
        if (hostCharacter != null)
        {
            // Host does a small nod or turn
            hostCharacter.LookAt(ecobot.transform.position);
        }
        yield return new WaitForSeconds(2.0f);

        // EcoBot starts routine
        currentPhase = "4. EcoBot cleaning in progress...";
        ecobot.StartRestaurantCleaningScenario(roomEntryWaypoint, itemsToClean, () => {
            currentPhase = "5. Room clean! EcoBot docked. Scenario Complete.";
            isSimulationRunning = false;
        });
    }

    private IEnumerator MoveActor(Transform actor, Vector3 target, float speed)
    {
        while (Vector3.Distance(actor.position, target) > 0.2f)
        {
            actor.position = Vector3.MoveTowards(actor.position, target, speed * Time.deltaTime);
            actor.LookAt(target);
            yield return null;
        }
        actor.gameObject.SetActive(false); // Guest has left
    }

    public void PromptHostGuidance(CleanableItem item, Action onDone)
    {
        pendingGuidanceItem = item;
        onGuidanceResolved = onDone;
        isAwaitingGuidance = true;
    }

    public void ResolveGuidance(bool keepOrDiscard)
    {
        if (!isAwaitingGuidance) return;
        isAwaitingGuidance = false;
        if (keepOrDiscard)
        {
            // Marked to pick up / safe
            if (pendingGuidanceItem != null)
            {
                pendingGuidanceItem.cleaningActionDescription = "Collecting and moving to Host Safe Box";
            }
        }
        else
        {
            // Ignored / left on table
            if (pendingGuidanceItem != null)
            {
                pendingGuidanceItem.cleaningActionDescription = "Leaving item safely in place";
            }
        }
        onGuidanceResolved?.Invoke();
    }

    void OnGUI()
    {
        // EcoBot Brand Colors from Design System: Primary #88A27C, Dark #191410, White #FDFDFD
        GUI.skin.box.fontSize = 14;
        GUI.skin.label.fontSize = 13;
        GUI.skin.button.fontSize = 13;

        // Top Banner / Status Panel
        GUILayout.BeginArea(new Rect(20, 20, 420, 260), GUI.skin.box);
        GUILayout.Label("<b>EcoBot — Restaurant Service Demo</b>", GUILayout.Height(24));
        GUILayout.Label($"<b>Scenario Phase:</b> {currentPhase}");
        GUILayout.Label($"<b>EcoBot State:</b> {ecobot.currentState}");
        GUILayout.Label($"<b>Status:</b> {ecobot.currentStatusMessage}");
        GUILayout.Label($"<b>Surface Sensor:</b> {ecobot.detectedSurface}");

        GUILayout.Space(10);
        if (!isSimulationRunning)
        {
            if (GUILayout.Button("▶ Start Restaurant Cleaning Scenario", GUILayout.Height(36)))
            {
                StartFullScenario();
            }
        }
        else
        {
            GUILayout.Label("<i>Scenario is actively running...</i>");
        }

        GUILayout.EndArea();

        // Human-in-the-Loop Confirmation Popup (Storyboards Page 3 & Survey Q03/Q09)
        if (isAwaitingGuidance && pendingGuidanceItem != null)
        {
            float w = 480;
            float h = 180;
            Rect centerRect = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            GUI.Box(centerRect, "<b>[Human-Robot Interaction] Confirmation Required</b>");

            GUILayout.BeginArea(new Rect(centerRect.x + 15, centerRect.y + 35, w - 30, h - 45));
            GUILayout.Label($"EcoBot detected an uncertain object: <b>{pendingGuidanceItem.itemName}</b>\n(Is it guest property or trash?)");
            GUILayout.Space(15);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("✔ Hand over to Host Lost & Found", GUILayout.Height(40)))
            {
                ResolveGuidance(true);
            }
            if (GUILayout.Button("✖ Leave on table untouched", GUILayout.Height(40)))
            {
                ResolveGuidance(false);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}

