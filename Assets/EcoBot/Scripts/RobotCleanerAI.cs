using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace EcoBot
{
    /// <summary>
    /// Autonomous navigation and cleaning controller for EcoBot.
    /// - Vacuumables: drives directly over them to vacuum them up.
    /// - Pickupables: stops in front, faces the item, picks it up with RobotArmController,
    ///   carries it to the drop-off location, places it down, and repeats until clean.
    /// - Pickupables tagged "Unsure" pause and ask the user before acting.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public class RobotCleanerAI : MonoBehaviour
    {
        public enum CleanerState
        {
            Idle,
            Scanning,
            MovingToPickupable,
            AligningForPickup,
            PickingUp,
            MovingToDropOff,
            DroppingOff,
            MovingToVacuumable,
            Vacuuming,
            Completed,
            Yielding,          // appended last so existing serialized values stay valid
            AwaitingGuidance   // NEW: robot paused, waiting for user to answer the prompt
        }

        [Header("References")]
        public RobotArmController arm;
        public NavMeshAgent agent;
        public Transform dropOffLocation;

        [Header("Layer Configuration")]
        public LayerMask pickupablesMask;
        public LayerMask vacuumablesMask;

        [Header("Navigation & Approach Tuning (Robot-Local Units)")]
        [Range(0.45f, 1.0f)] public float localPickupApproachDistance = 0.72f;
        public float localVacuumCollectRadius = 0.6f;
        public float alignmentTurnSpeed = 240f;
        public float vacuumPauseDuration = 0.25f;
        public bool autoStartOnPlay = true;

        [Header("Dynamic Obstacle Avoidance (Sensor)")]
        public bool enableObstacleAvoidance = true;
        public LayerMask obstacleMask;
        public float localObstacleDetectionDistance = 2.5f;
        public float localObstacleSensorRadius = 0.6f;
        public float localObstacleSensorHeight = 0.5f;
        public float clearPathDelay = 0.5f;
        public float yieldTurnSpeed = 180f;
        public float blockedHelpAfterSeconds = 6f;

        [Header("Live Re-scan")]
        public float rescanInterval = 1.5f;

        [Header("Events (hook UI here)")]
        public UnityEvent<string> onStatusMessage;

        [Header("Runtime Status (Read-Only)")]
        [SerializeField] private CleanerState currentState = CleanerState.Idle;
        [SerializeField] private GameObject currentTarget;
        [SerializeField] private List<GameObject> remainingTargets = new List<GameObject>();

        private readonly HashSet<GameObject> deliveredObjects = new HashSet<GameObject>();
        private readonly HashSet<GameObject> skippedObjects = new HashSet<GameObject>();
        private readonly HashSet<GameObject> guidanceResolved = new HashSet<GameObject>(); // NEW

        private readonly Collider[] obstacleBuffer = new Collider[16];
        private CleanerState stateBeforeYield;
        private float resumeAfter;
        private float yieldStartTime;
        private bool helpRequested;
        private float nextRescanTime;
        private Vector3 lastTargetPosition;

        public CleanerState State => currentState;
        public GameObject CurrentTarget => currentTarget;

        float WorldScale => Mathf.Max(0.05f, Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z)));
        float WorldApproachDistance => localPickupApproachDistance * WorldScale;
        float WorldVacuumRadius => localVacuumCollectRadius * WorldScale;

        void Reset() => AutoAssignReferencesAndLayers();
        void Awake() => AutoAssignReferencesAndLayers();

        void AutoAssignReferencesAndLayers()
        {
            if (!arm) arm = GetComponent<RobotArmController>();
            if (!agent) agent = GetComponent<NavMeshAgent>();

            if (pickupablesMask.value == 0)
            {
                int pLayer = LayerMask.NameToLayer("Pickupables");
                if (pLayer != -1) pickupablesMask = 1 << pLayer;
            }
            if (vacuumablesMask.value == 0)
            {
                int vLayer = LayerMask.NameToLayer("Vacuumables");
                if (vLayer != -1) vacuumablesMask = 1 << vLayer;
            }
            if (obstacleMask.value == 0)
            {
                int aLayer = LayerMask.NameToLayer("Avoidables");
                if (aLayer != -1) obstacleMask = 1 << aLayer;
            }
        }

        void Start()
        {
            if (arm)
            {
                arm.autoPickupByTag = false;
                arm.runTestOnStart = false;
                if (!dropOffLocation && arm.testDropPoint) dropOffLocation = arm.testDropPoint;
                arm.testDropPoint = null;
            }

            if (!agent)
            {
                Debug.LogError("[RobotCleanerAI] No NavMeshAgent found on this robot.", this);
                enabled = false;
                return;
            }

            if (agent && !agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
                    agent.Warp(hit.position);
            }

            if (autoStartOnPlay) StartCleaning();
        }

        [ContextMenu("Start Cleaning")]
        public void StartCleaning()
        {
            StopAllCoroutines();
            currentTarget = null;
            ScanRoom();
            SelectAndMoveToNextTarget();
        }

        public void ScanRoom()
        {
            currentState = CleanerState.Scanning;
            remainingTargets.Clear();
            AddNewTargets();
            Debug.Log($"[RobotCleanerAI] Room scan complete. Found {remainingTargets.Count} target(s).");
        }

        int AddNewTargets()
        {
            int combinedMask = pickupablesMask.value | vacuumablesMask.value;
            Collider[] allColliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
            int added = 0;

            foreach (Collider col in allColliders)
            {
                if (!col || !col.gameObject.activeInHierarchy) continue;
                if (col.transform.IsChildOf(transform)) continue;

                GameObject rootCandidate = ResolveTargetRoot(col, combinedMask);
                if (!rootCandidate) continue;
                if (deliveredObjects.Contains(rootCandidate) || skippedObjects.Contains(rootCandidate)) continue;
                if (rootCandidate == currentTarget || remainingTargets.Contains(rootCandidate)) continue;
                if (arm && arm.HeldObject == rootCandidate) continue;

                remainingTargets.Add(rootCandidate);
                added++;
            }
            return added;
        }

        GameObject ResolveTargetRoot(Collider col, int layerMask)
        {
            if (col.attachedRigidbody && IsInLayerMask(col.attachedRigidbody.gameObject.layer, layerMask))
                return col.attachedRigidbody.gameObject;

            Transform t = col.transform;
            GameObject match = null;
            while (t != null)
            {
                if (IsInLayerMask(t.gameObject.layer, layerMask)) match = t.gameObject;
                t = t.parent;
            }
            return match;
        }

        static bool IsInLayerMask(int layer, int layerMask) => (layerMask & (1 << layer)) != 0;

        void SelectAndMoveToNextTarget()
        {
            remainingTargets.RemoveAll(item => item == null || !item.activeInHierarchy ||
                                               deliveredObjects.Contains(item) || skippedObjects.Contains(item));

            if (remainingTargets.Count == 0)
            {
                currentTarget = null;
                currentState = CleanerState.Completed;
                if (agent && agent.isOnNavMesh) agent.isStopped = true;
                Debug.Log("[RobotCleanerAI] All Pickupables and Vacuumables have been cleaned!");
                return;
            }

            float bestDistSqr = float.MaxValue;
            int bestIndex = 0;
            for (int i = 0; i < remainingTargets.Count; i++)
            {
                float d = (remainingTargets[i].transform.position - transform.position).sqrMagnitude;
                if (d < bestDistSqr) { bestDistSqr = d; bestIndex = i; }
            }

            currentTarget = remainingTargets[bestIndex];
            remainingTargets.RemoveAt(bestIndex);
            lastTargetPosition = currentTarget.transform.position;

            bool isPickupable = IsInLayerMask(currentTarget.layer, pickupablesMask.value);

            if (isPickupable)
            {
                Vector3 approachPoint = GetTargetApproachPosition(currentTarget.transform, WorldApproachDistance);
                agent.stoppingDistance = 0.02f;
                agent.isStopped = false;
                agent.SetDestination(approachPoint);
                currentState = CleanerState.MovingToPickupable;
                Debug.Log($"[RobotCleanerAI] Moving to Pickupable: {currentTarget.name}");
            }
            else
            {
                Vector3 targetPoint = SampleNavMesh(currentTarget.transform.position, 2.0f);
                agent.stoppingDistance = 0.02f;
                agent.isStopped = false;
                agent.SetDestination(targetPoint);
                currentState = CleanerState.MovingToVacuumable;
                Debug.Log($"[RobotCleanerAI] Moving to Vacuumable: {currentTarget.name}");
            }
        }

        void Update()
        {
            if (arm && arm.IsBusy)
            {
                if (agent && agent.isOnNavMesh && !agent.isStopped) agent.isStopped = true;
                return;
            }

            if (currentState == CleanerState.Yielding) { UpdateYielding(); return; }
            if (currentState == CleanerState.AwaitingGuidance) return; // driven by PickupSequenceRoutine

            if (IsTravelState(currentState) && agent.isOnNavMesh && !agent.pathPending
                && agent.remainingDistance > WorldApproachDistance)
            {
                Collider blocker = DetectObstacleAhead();
                if (blocker) { BeginYield(blocker); return; }
            }

            if (IsTravelState(currentState) || currentState == CleanerState.Completed) TickLiveRescan();

            switch (currentState)
            {
                case CleanerState.MovingToPickupable:
                    if (!currentTarget || !currentTarget.activeInHierarchy) { SelectAndMoveToNextTarget(); return; }
                    if (HasArrived()) { agent.isStopped = true; StartCoroutine(PickupSequenceRoutine()); }
                    break;

                case CleanerState.MovingToDropOff:
                    if (HasArrived()) { agent.isStopped = true; StartCoroutine(DropOffSequenceRoutine()); }
                    break;

                case CleanerState.MovingToVacuumable:
                    if (!currentTarget || !currentTarget.activeInHierarchy) { SelectAndMoveToNextTarget(); return; }
                    float horizontalDist = Vector2.Distance(
                        new Vector2(transform.position.x, transform.position.z),
                        new Vector2(currentTarget.transform.position.x, currentTarget.transform.position.z));
                    if (horizontalDist <= WorldVacuumRadius || HasArrived())
                    {
                        agent.isStopped = true;
                        StartCoroutine(VacuumSequenceRoutine());
                    }
                    break;
            }
        }

        static bool IsTravelState(CleanerState state) =>
            state == CleanerState.MovingToPickupable ||
            state == CleanerState.MovingToDropOff ||
            state == CleanerState.MovingToVacuumable;

        Vector3 RouteDirection()
        {
            Vector3 dir = agent.steeringTarget - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) { dir = agent.velocity; dir.y = 0f; }
            if (dir.sqrMagnitude < 0.0001f) { dir = transform.forward; dir.y = 0f; }
            return dir.normalized;
        }

        Collider DetectObstacleAhead()
        {
            if (!enableObstacleAvoidance || obstacleMask.value == 0 || !agent) return null;

            float s = WorldScale;
            Vector3 start = transform.position + Vector3.up * (localObstacleSensorHeight * s);
            Vector3 end = start + RouteDirection() * (localObstacleDetectionDistance * s);
            float radius = localObstacleSensorRadius * s;

            int count = Physics.OverlapCapsuleNonAlloc(start, end, radius, obstacleBuffer,
                obstacleMask, QueryTriggerInteraction.Ignore);

            Collider closest = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider col = obstacleBuffer[i];
                if (!col || !col.gameObject.activeInHierarchy) continue;
                if (col.transform.IsChildOf(transform)) continue;
                if (arm && arm.HeldObject && col.transform.IsChildOf(arm.HeldObject.transform)) continue;
                if (currentTarget && col.transform.IsChildOf(currentTarget.transform)) continue;

                float sqr = (col.ClosestPoint(start) - start).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; closest = col; }
            }
            return closest;
        }

        void BeginYield(Collider blocker)
        {
            stateBeforeYield = currentState;
            currentState = CleanerState.Yielding;
            agent.isStopped = true;
            yieldStartTime = Time.time;
            resumeAfter = Time.time + clearPathDelay;
            helpRequested = false;
            Say($"Path blocked by '{blocker.name}'. Waiting for it to clear.");
        }

        void UpdateYielding()
        {
            Collider blocker = DetectObstacleAhead();
            if (blocker)
            {
                resumeAfter = Time.time + clearPathDelay;
                FaceTowards(blocker.bounds.center);
                if (!helpRequested && Time.time - yieldStartTime >= blockedHelpAfterSeconds)
                {
                    helpRequested = true;
                    Say($"Still blocked by '{blocker.name}'. Could you please move it or guide me?");
                }
                return;
            }
            if (Time.time >= resumeAfter) EndYield();
        }

        void EndYield()
        {
            currentState = stateBeforeYield;
            bool needsTarget = currentState != CleanerState.MovingToDropOff;
            if (needsTarget && (!currentTarget || !currentTarget.activeInHierarchy))
            {
                SelectAndMoveToNextTarget();
                return;
            }
            agent.isStopped = false;
            agent.SetDestination(ComputeDestination(currentState));
            Say("Path clear. Resuming.");
        }

        Vector3 ComputeDestination(CleanerState travelState)
        {
            switch (travelState)
            {
                case CleanerState.MovingToPickupable: return GetTargetApproachPosition(currentTarget.transform, WorldApproachDistance);
                case CleanerState.MovingToVacuumable: return SampleNavMesh(currentTarget.transform.position, 2.0f);
                case CleanerState.MovingToDropOff: return GetDropOffApproachPosition(dropOffLocation);
                default: return agent.destination;
            }
        }

        void FaceTowards(Vector3 worldPoint)
        {
            Vector3 flat = worldPoint - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(flat.normalized, Vector3.up), yieldTurnSpeed * Time.deltaTime);
        }

        void Say(string message)
        {
            Debug.Log($"[RobotCleanerAI] {message}");
            onStatusMessage?.Invoke(message);
        }

        void TickLiveRescan()
        {
            if (Time.time < nextRescanTime) return;
            nextRescanTime = Time.time + rescanInterval;

            int added = AddNewTargets();

            if (currentState == CleanerState.Completed)
            {
                if (added > 0)
                {
                    Say($"Found {added} new item(s). Resuming cleaning.");
                    SelectAndMoveToNextTarget();
                }
                return;
            }

            if (currentTarget && currentState != CleanerState.MovingToDropOff &&
                (currentTarget.transform.position - lastTargetPosition).sqrMagnitude > 0.01f)
            {
                lastTargetPosition = currentTarget.transform.position;
                agent.SetDestination(ComputeDestination(currentState));
            }
        }

        /// <summary>Called by GuidancePromptUI when the user answers.</summary>
        public void ProvideGuidance(GameObject target, bool approve)
        {
            if (!target) return;

            if (!approve)
            {
                skippedObjects.Add(target);
                guidanceResolved.Add(target);    // NEW: unblock AwaitGuidance()
                Say($"Leaving '{target.name}' untouched.");
                return;
            }

            if (arm) arm.ApproveByUser(target);
            skippedObjects.Remove(target);
            if (!remainingTargets.Contains(target)) remainingTargets.Add(target);
            guidanceResolved.Add(target);        // NEW: unblock AwaitGuidance()

            if (currentState == CleanerState.Completed || currentState == CleanerState.Idle)
                SelectAndMoveToNextTarget();
        }

        /// <summary>Called by DraggableItem when the human player moves an object.</summary>
        public void NotifyObjectMoved(GameObject obj) // NEW
        {
            if (!obj) return;
            if (arm != null && (arm.HeldObject == obj || (arm.IsBusy && currentTarget == obj))) return;

            skippedObjects.Remove(obj);
            if (!remainingTargets.Contains(obj) && !deliveredObjects.Contains(obj)
                && obj != currentTarget && obj.activeInHierarchy)
                remainingTargets.Add(obj);

            if ((currentState == CleanerState.Completed || currentState == CleanerState.Idle)
                && remainingTargets.Count > 0)
                SelectAndMoveToNextTarget();
        }

        bool HasArrived()
        {
            if (!agent || !agent.isOnNavMesh || agent.pathPending) return false;
            return agent.remainingDistance <= agent.stoppingDistance + 0.05f;
        }

        IEnumerator PickupSequenceRoutine()
        {
            currentState = CleanerState.AligningForPickup;

            if (currentTarget)
            {
                yield return RotateTowardsPointRoutine(currentTarget.transform.position);

                Vector3 flatToTarget = currentTarget.transform.position - transform.position;
                flatToTarget.y = 0f;
                float currentFlatDist = flatToTarget.magnitude;
                float idealDist = WorldApproachDistance;
                if (Mathf.Abs(currentFlatDist - idealDist) > 0.03f && flatToTarget.sqrMagnitude > 0.0001f)
                {
                    Vector3 adjustedPos = currentTarget.transform.position - flatToTarget.normalized * idealDist;
                    adjustedPos.y = transform.position.y;
                    if (NavMesh.SamplePosition(adjustedPos, out NavMeshHit hit, 0.3f, NavMesh.AllAreas))
                    {
                        float moveSpeed = agent ? agent.speed : 0.5f;
                        float elapsed = 0f;
                        while (elapsed < 2f && Vector3.Distance(transform.position, hit.position) > 0.01f)
                        {
                            Vector3 next = Vector3.MoveTowards(transform.position, hit.position, moveSpeed * Time.deltaTime);
                            agent.Move(next - transform.position);
                            elapsed += Time.deltaTime;
                            yield return null;
                        }
                    }
                }
                yield return RotateTowardsPointRoutine(currentTarget.transform.position);
            }

            // NEW: pause and prompt the user if the item is on Pickupables AND tagged Unsure.
            if (currentTarget && arm && !arm.ShouldPickUpWithoutGuidance(currentTarget))
            {
                yield return AwaitGuidance(currentTarget);

                if (!currentTarget || skippedObjects.Contains(currentTarget))
                {
                    SelectAndMoveToNextTarget();
                    yield break;
                }
            }

            currentState = CleanerState.PickingUp;
            if (arm && currentTarget)
            {
                GameObject attempted = currentTarget;
                bool started = arm.PickUp(attempted, dropPoint: null);
                if (started) yield return new WaitUntil(() => !arm.IsBusy);
                else { skippedObjects.Add(attempted); Say($"Could not pick up '{attempted.name}'. Skipping it."); }
            }

            if (arm && arm.HeldObject != null && dropOffLocation != null)
            {
                Vector3 dropApproach = GetDropOffApproachPosition(dropOffLocation);
                agent.stoppingDistance = 0.05f;
                agent.isStopped = false;
                agent.SetDestination(dropApproach);
                currentState = CleanerState.MovingToDropOff;
                Debug.Log($"[RobotCleanerAI] Carrying {arm.HeldObject.name} to drop-off ({dropOffLocation.name}).");
            }
            else SelectAndMoveToNextTarget();
        }

        // NEW: pause and wait for ProvideGuidance() to add to guidanceResolved.
        IEnumerator AwaitGuidance(GameObject target)
        {
            currentState = CleanerState.AwaitingGuidance;
            Say($"Not sure about '{target.name}'. Waiting for your guidance.");

            arm.AskUserForGuidance(target); // raises onGuidanceNeeded -> GuidancePromptUI listens

            yield return new WaitUntil(() =>
                guidanceResolved.Contains(target) ||
                target == null ||
                !target.activeInHierarchy);

            guidanceResolved.Remove(target);
        }

        IEnumerator DropOffSequenceRoutine()
        {
            currentState = CleanerState.DroppingOff;
            if (dropOffLocation) yield return RotateTowardsPointRoutine(dropOffLocation.position);

            if (arm && arm.HeldObject != null)
            {
                GameObject droppedObj = arm.HeldObject;
                deliveredObjects.Add(droppedObj);

                Vector3 placeSpot = transform.position + transform.forward * WorldApproachDistance;
                placeSpot.y = transform.position.y;
                arm.PlaceAt(placeSpot);
                yield return new WaitUntil(() => !arm.IsBusy);
            }
            SelectAndMoveToNextTarget();
        }

        IEnumerator VacuumSequenceRoutine()
        {
            currentState = CleanerState.Vacuuming;
            if (vacuumPauseDuration > 0f) yield return new WaitForSeconds(vacuumPauseDuration);
            if (currentTarget) { Destroy(currentTarget); currentTarget = null; }
            SelectAndMoveToNextTarget();
        }

        IEnumerator RotateTowardsPointRoutine(Vector3 worldPoint)
        {
            Vector3 flatDir = worldPoint - transform.position;
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude < 0.0001f) yield break;

            Quaternion targetRotation = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
            while (Quaternion.Angle(transform.rotation, targetRotation) > 1.5f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, alignmentTurnSpeed * Time.deltaTime);
                yield return null;
            }
            transform.rotation = targetRotation;
        }

        Vector3 GetTargetApproachPosition(Transform target, float stopDistance)
        {
            Vector3 targetPos = target.position;
            Vector3 dirToRobot = transform.position - targetPos;
            dirToRobot.y = 0f;
            if (dirToRobot.sqrMagnitude < 0.001f) dirToRobot = -transform.forward;
            else dirToRobot.Normalize();

            Vector3 desiredStopPos = targetPos + dirToRobot * stopDistance;
            desiredStopPos.y = transform.position.y;
            return SampleNavMesh(desiredStopPos, 1.5f);
        }

        Vector3 GetDropOffApproachPosition(Transform dropTarget)
        {
            Collider dropCol = dropTarget.GetComponentInChildren<Collider>();
            if (dropCol)
            {
                Vector3 surfacePoint = dropCol.ClosestPoint(transform.position);
                surfacePoint.y = transform.position.y;
                Vector3 dirOut = transform.position - dropCol.bounds.center;
                dirOut.y = 0f;
                if (dirOut.sqrMagnitude < 0.001f) dirOut = Vector3.forward;
                dirOut.Normalize();
                return SampleNavMesh(surfacePoint + dirOut * (WorldApproachDistance + 0.1f), 2.0f);
            }
            return GetTargetApproachPosition(dropTarget, WorldApproachDistance);
        }

        Vector3 SampleNavMesh(Vector3 point, float maxDistance)
        {
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, maxDistance, NavMesh.AllAreas)) return hit.position;
            return point;
        }

        void OnDrawGizmosSelected()
        {
            float s = WorldScale;
            Vector3 dir = transform.forward;
            if (Application.isPlaying && agent && agent.isOnNavMesh) dir = RouteDirection();

            Vector3 start = transform.position + Vector3.up * (localObstacleSensorHeight * s);
            Vector3 end = start + dir * (localObstacleDetectionDistance * s);
            float radius = localObstacleSensorRadius * s;

            Gizmos.color = currentState == CleanerState.Yielding ? Color.red : Color.yellow;
            Gizmos.DrawWireSphere(start, radius);
            Gizmos.DrawWireSphere(end, radius);
            Gizmos.DrawLine(start, end);
        }
    }
}