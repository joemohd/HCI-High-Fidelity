using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum EcoBotActionState
{
    IdleAtDock,
    MovingToRoom,
    InspectingRoom,
    CleaningItem,
    WaitingForHumanGuidance,
    ReturningToDock,
    Finished
}

public class EcoBotController : MonoBehaviour
{
    [Header("Visual Components")]
    public Transform robotBody;
    public Transform roboticArm;
    public Transform armClaw;
    public Transform scannerCone;
    public LineRenderer laserBeam;

    [Header("Movement & Specs")]
    public float moveSpeed = 3.5f;
    public float rotateSpeed = 8f;
    public Transform chargingStation;

    [Header("Current State")]
    public EcoBotActionState currentState = EcoBotActionState.IdleAtDock;
    public string currentStatusMessage = "Docked & Ready";
    public string detectedSurface = "Tile Floor";

    private Coroutine currentSequenceCoroutine;

    void Awake()
    {
        if (scannerCone != null) scannerCone.gameObject.SetActive(false);
        if (laserBeam != null) laserBeam.enabled = false;
    }

    public void StartRestaurantCleaningScenario(Transform roomEntry, List<CleanableItem> itemsToClean, System.Action onComplete = null)
    {
        if (currentSequenceCoroutine != null)
        {
            StopCoroutine(currentSequenceCoroutine);
        }
        currentSequenceCoroutine = StartCoroutine(CleanScenarioRoutine(roomEntry, itemsToClean, onComplete));
    }

    private IEnumerator CleanScenarioRoutine(Transform roomEntry, List<CleanableItem> itemsToClean, System.Action onComplete)
    {
        // 1. Undock & Go to Room
        currentState = EcoBotActionState.MovingToRoom;
        currentStatusMessage = "Host called EcoBot: Navigating to Dining Area...";
        yield return MoveToPosition(roomEntry.position);

        // 2. Inspecting Environment
        currentState = EcoBotActionState.InspectingRoom;
        currentStatusMessage = "Perceiving Dining Room: Scanning tables and floor...";
        if (scannerCone != null) scannerCone.gameObject.SetActive(true);

        // Rotate in circle scanning
        float scanTime = 2.0f;
        float elapsed = 0f;
        while (elapsed < scanTime)
        {
            transform.Rotate(0, 180f * Time.deltaTime, 0);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (scannerCone != null) scannerCone.gameObject.SetActive(false);

        // 3. Process each item (napkins, bottles, crumbs, or uncertain items)
        foreach (var item in itemsToClean)
        {
            if (item == null || item.isCleaned) continue;

            // Approach item safely
            currentState = EcoBotActionState.MovingToRoom;
            currentStatusMessage = $"Approaching target: {item.itemName}";
            Vector3 targetPos = item.transform.position;
            // Stop slightly before the item
            Vector3 approachPos = targetPos + (transform.position - targetPos).normalized * 0.8f;
            approachPos.y = transform.position.y;
            yield return MoveToPosition(approachPos);
            yield return LookAtTarget(targetPos);

            // If item requires human approval (e.g. uncertain object like guest's forgotten wallet/phone or wet hazard)
            if (item.requiresHumanConfirmation)
            {
                currentState = EcoBotActionState.WaitingForHumanGuidance;
                currentStatusMessage = $"[HRI Alert] Uncertain item detected: '{item.itemName}'! Host confirmation requested.";
                
                // Prompt simulated via UI event or wait for interaction
                bool confirmed = false;
                RestaurantScenarioManager.Instance.PromptHostGuidance(item, () => {
                    confirmed = true;
                });

                while (!confirmed)
                {
                    yield return null;
                }
            }

            // Clean / Pick up item
            currentState = EcoBotActionState.CleaningItem;
            currentStatusMessage = $"Performing action: {item.cleaningActionDescription} on {item.itemName}...";

            // Robotic arm pickup animation
            yield return AnimateArmPickup(item);
            item.CleanUp();
            yield return new WaitForSeconds(0.6f);
        }

        // 4. Return to Dock
        currentState = EcoBotActionState.ReturningToDock;
        currentStatusMessage = "Dining area clean! Returning to Charging Station...";
        if (chargingStation != null)
        {
            yield return MoveToPosition(chargingStation.position);
            yield return LookAtTarget(chargingStation.position + chargingStation.forward);
        }

        currentState = EcoBotActionState.Finished;
        currentStatusMessage = "Docked. Ready for next call.";
        onComplete?.Invoke();
    }

    private IEnumerator MoveToPosition(Vector3 destination)
    {
        destination.y = transform.position.y;
        while (Vector3.Distance(transform.position, destination) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, destination, moveSpeed * Time.deltaTime);
            Vector3 direction = (destination - transform.position).normalized;
            if (direction != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotateSpeed * Time.deltaTime);
            }
            yield return null;
        }
    }

    private IEnumerator LookAtTarget(Vector3 target)
    {
        target.y = transform.position.y;
        Vector3 direction = (target - transform.position).normalized;
        if (direction != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(direction);
            float t = 0f;
            while (t < 0.5f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotateSpeed * Time.deltaTime);
                t += Time.deltaTime;
                yield return null;
            }
        }
    }

    private IEnumerator AnimateArmPickup(CleanableItem item)
    {
        if (roboticArm == null) yield break;

        Vector3 originalArmRot = roboticArm.localEulerAngles;
        // Reach down
        float t = 0f;
        while (t < 0.4f)
        {
            roboticArm.localEulerAngles = Vector3.Lerp(originalArmRot, new Vector3(45f, 0, 0), t / 0.4f);
            t += Time.deltaTime;
            yield return null;
        }

        yield return new WaitForSeconds(0.3f);

        // Retract
        t = 0f;
        while (t < 0.4f)
        {
            roboticArm.localEulerAngles = Vector3.Lerp(new Vector3(45f, 0, 0), originalArmRot, t / 0.4f);
            t += Time.deltaTime;
            yield return null;
        }
        roboticArm.localEulerAngles = originalArmRot;
    }
}

