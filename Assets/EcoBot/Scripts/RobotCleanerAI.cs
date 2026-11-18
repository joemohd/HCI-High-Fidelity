using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace EcoBot
{
    /// <summary>
    /// Autonomous navigation and cleaning controller for EcoBot.
    /// Scans the room for objects on the "Pickupables" and "Vacuumables" layers:
    /// - Vacuumables: drives directly over them to vacuum them up.
    /// - Pickupables: stops at a reachable distance in front, faces the item, picks it up with RobotArmController,
    ///   carries it to the drop-off location, places it down, and repeats until the room is clean.
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
            Yielding   // appended last so existing serialized values stay valid
        }

        [Header("References")]
        [Tooltip("Reference to the RobotArmController on this robot (auto-filled if empty).")]
        public RobotArmController arm;

        [Tooltip("Reference to the NavMeshAgent on this robot (auto-filled if empty).")]
        public NavMeshAgent agent;

        [Tooltip("Where the robot brings Pickupable items. If empty, inherits testDropPoint from RobotArmController.")]
        public Transform dropOffLocation;

        [Header("Layer Configuration")]
        [Tooltip("Layer mask for objects the arm should pick up and carry to DropOffLocation.")]
        public LayerMask pickupablesMask;

        [Tooltip("Layer mask for small debris/dust the robot should drive directly over to vacuum.")]
        public LayerMask vacuumablesMask;

        [Header("Navigation & Approach Tuning (Robot-Local Units)")]
        [Tooltip("Desired stopping distance in front of a Pickupable item in robot-local units (0.7 local = ~0.175m world at 0.25x scale).")]
        [Range(0.45f, 1.0f)]
        public float localPickupApproachDistance = 0.72f;

        [Tooltip("Distance in robot-local units from a Vacuumable item at which it gets sucked up.")]
        public float localVacuumCollectRadius = 0.6f;

        [Tooltip("How fast (degrees/sec) the robot rotates in place to center the target before reaching with the arm.")]
        public float alignmentTurnSpeed = 240f;

        [Tooltip("Brief pause (seconds) when vacuuming over a Vacuumable item.")]
        public float vacuumPauseDuration = 0.25f;

        [Tooltip("Start scanning and cleaning automatically when Play begins.")]
        public bool autoStartOnPlay = true;

        [Header("Dynamic Obstacle Avoidance (Sensor)")]
        [Tooltip("Sense obstacles ahead every frame and stop/yield instead of driving into them.")]
        public bool enableObstacleAvoidance = true;

        [Tooltip("Layers the sensor reacts to (people, moving toys, anything NOT baked into the NavMesh). Defaults to the 'Avoidables' layer. Do NOT include Pickupables/Vacuumables or static furniture.")]
        public LayerMask obstacleMask;

        [Tooltip("How far ahead along the route the sensor looks (robot-local units).")]
        public float localObstacleDetectionDistance = 2.5f;

        [Tooltip("Radius of the sensor capsule (robot-local units).")]
        public float localObstacleSensorRadius = 0.6f;

        [Tooltip("Height of the sensor capsule above the floor (robot-local units).")]
        public float localObstacleSensorHeight = 0.5f;

        [Tooltip("The path must stay clear this many seconds before the robot resumes (prevents flickering).")]
        public float clearPathDelay = 0.5f;

        [Tooltip("Degrees/sec the robot turns to face a blocking obstacle while waiting.")]
        public float yieldTurnSpeed = 180f;

        [Tooltip("If still blocked after this many seconds, the robot asks the user for help.")]
        public float blockedHelpAfterSeconds = 6f;

        [Header("Live Re-scan")]
        [Tooltip("Seconds between re-scans for new/moved targets while cleaning.")]
        public float rescanInterval = 1.5f;

        [Header("Events (hook UI here)")]
        [Tooltip("Human-readable status, e.g. 'Path blocked - waiting'. Connect to a text/notification UI.")]
        public UnityEvent<string> onStatusMessage;

        [Header("Runtime Status (Read-Only)")]
        [SerializeField] private CleanerState currentState = CleanerState.Idle;
        [SerializeField] private GameObject currentTarget;
        [SerializeField] private List<GameObject> remainingTargets = new List<GameObject>();

        private readonly HashSet<GameObject> deliveredObjects = new HashSet<GameObject>();

        // Objects the robot is unsure about / could not pick up. They are left alone until the user decides.
        private readonly HashSet<GameObject> skippedObjects = new HashSet<GameObject>();

        // Obstacle-avoidance runtime state
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

        void Reset()
        {
            AutoAssignReferencesAndLayers();
        }

        void Awake()
        {
            AutoAssignReferencesAndLayers();
        }

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
                // Disable automatic proximity/test triggers so RobotCleanerAI controls when the arm picks & drops
                arm.autoPickupByTag = false;
                arm.runTestOnStart = false;

                // Inherit testDropPoint from the arm if dropOffLocation isn't set,
                // and clear arm.testDropPoint so PickUp() holds the item upright until we drive to dropOffLocation.
                if (!dropOffLocation && arm.testDropPoint)
                {
                    dropOffLocation = arm.testDropPoint;
                }
                arm.testDropPoint = null;
            }

            if (!agent)
            {
                Debug.LogError("[RobotCleanerAI] No NavMeshAgent found on this robot.", this);
                enabled = false;
                return;
            }

            // Ensure agent is snapped onto the NavMesh at start
            if (agent && !agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                }
            }

            if (autoStartOnPlay)
            {
                StartCleaning();
            }
        }

        /// <summary>
        /// Scans the entire scene for objects on Pickupables and Vacuumables layers and begins cleaning.
        /// </summary>
        [ContextMenu("Start Cleaning")]
        public void StartCleaning()
        {
            StopAllCoroutines();
            currentTarget = null;
            ScanRoom();
            SelectAndMoveToNextTarget();
        }

        /// <summary>
        /// Finds all active GameObjects with colliders on the Pickupables or Vacuumables layers.
        /// </summary>
        public void ScanRoom()
        {
            currentState = CleanerState.Scanning;
            remainingTargets.Clear();
            AddNewTargets();
            Debug.Log($"[RobotCleanerAI] Room scan complete. Found {remainingTargets.Count} target(s).");
        }

        /// <summary>
        /// Adds any Pickupable/Vacuumable objects that are not already queued, delivered or skipped.
        /// Used by the initial scan and by the live re-scan, so toys added or moved at runtime are noticed.
        /// </summary>
        /// <returns>Number of new targets added.</returns>
        int AddNewTargets()
        {
            int combinedMask = pickupablesMask.value | vacuumablesMask.value;
            Collider[] allColliders = FindObjectsByType<Collider>();
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
            {
                return col.attachedRigidbody.gameObject;
            }

            Transform t = col.transform;
            GameObject match = null;
            while (t != null)
            {
                if (IsInLayerMask(t.gameObject.layer, layerMask))
                    match = t.gameObject;
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

            // Pick the closest target to the robot's current position
            float bestDistSqr = float.MaxValue;
            int bestIndex = 0;
            for (int i = 0; i < remainingTargets.Count; i++)
            {
                float d = (remainingTargets[i].transform.position - transform.position).sqrMagnitude;
                if (d < bestDistSqr)
                {
                    bestDistSqr = d;
                    bestIndex = i;
                }
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
            // Never move while the arm is actively reaching/lifting/placing
            if (arm && arm.IsBusy)
            {
                if (agent && agent.isOnNavMesh && !agent.isStopped)
                    agent.isStopped = true;
                return;
            }

            // SENSE -> THINK -> ACT: while waiting for an obstacle to clear, only the yield logic runs
            if (currentState == CleanerState.Yielding)
            {
                UpdateYielding();
                return;
            }

            // SENSE: look along the route for something on the obstacle layers
            if (IsTravelState(currentState) && agent.isOnNavMesh && !agent.pathPending
                && agent.remainingDistance > WorldApproachDistance)
            {
                Collider blocker = DetectObstacleAhead();
                if (blocker)
                {
                    BeginYield(blocker);   // THINK + ACT: stop and wait
                    return;
                }
            }

            // Keep the target list and destination in sync with the live scene
            if (IsTravelState(currentState) || currentState == CleanerState.Completed)
            {
                TickLiveRescan();
            }

            switch (currentState)
            {
                case CleanerState.MovingToPickupable:
                    if (!currentTarget || !currentTarget.activeInHierarchy)
                    {
                        SelectAndMoveToNextTarget();
                        return;
                    }

                    if (HasArrived())
                    {
                        agent.isStopped = true;
                        StartCoroutine(PickupSequenceRoutine());
                    }
                    break;

                case CleanerState.MovingToDropOff:
                    if (HasArrived())
                    {
                        agent.isStopped = true;
                        StartCoroutine(DropOffSequenceRoutine());
                    }
                    break;

                case CleanerState.MovingToVacuumable:
                    if (!currentTarget || !currentTarget.activeInHierarchy)
                    {
                        SelectAndMoveToNextTarget();
                        return;
                    }

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

        // ------------------------------------------------------------------ obstacle sensing & yielding

        static bool IsTravelState(CleanerState state) =>
            state == CleanerState.MovingToPickupable ||
            state == CleanerState.MovingToDropOff ||
            state == CleanerState.MovingToVacuumable;

        /// <summary>Direction of travel along the NavMesh route (not where the model faces).</summary>
        Vector3 RouteDirection()
        {
            Vector3 dir = agent.steeringTarget - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = agent.velocity;
                dir.y = 0f;
            }
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = transform.forward;
                dir.y = 0f;
            }
            return dir.normalized;
        }

        /// <summary>
        /// SENSE: returns the closest collider on the obstacle layers inside a capsule that points
        /// along the route, or null if the way is clear. Ignores the robot itself, the object it
        /// carries, and the object it is heading to.
        /// </summary>
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
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    closest = col;
                }
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
                // Still blocked: restart the clear-path countdown and keep facing the obstacle
                resumeAfter = Time.time + clearPathDelay;
                FaceTowards(blocker.bounds.center);

                if (!helpRequested && Time.time - yieldStartTime >= blockedHelpAfterSeconds)
                {
                    helpRequested = true;
                    Say($"Still blocked by '{blocker.name}'. Could you please move it or guide me?");
                }
                return;
            }

            // Clear for a full clearPathDelay -> resume
            if (Time.time >= resumeAfter)
            {
                EndYield();
            }
        }

        void EndYield()
        {
            currentState = stateBeforeYield;

            // Target may have been removed or collected while we waited
            bool needsTarget = currentState != CleanerState.MovingToDropOff;
            if (needsTarget && (!currentTarget || !currentTarget.activeInHierarchy))
            {
                SelectAndMoveToNextTarget();
                return;
            }

            // Re-plan from where we stand: the NavMesh may have changed (e.g. a carving obstacle moved)
            agent.isStopped = false;
            agent.SetDestination(ComputeDestination(currentState));
            Say("Path clear. Resuming.");
        }

        Vector3 ComputeDestination(CleanerState travelState)
        {
            switch (travelState)
            {
                case CleanerState.MovingToPickupable:
                    return GetTargetApproachPosition(currentTarget.transform, WorldApproachDistance);
                case CleanerState.MovingToVacuumable:
                    return SampleNavMesh(currentTarget.transform.position, 2.0f);
                case CleanerState.MovingToDropOff:
                    return GetDropOffApproachPosition(dropOffLocation);
                default:
                    return agent.destination;
            }
        }

        void FaceTowards(Vector3 worldPoint)
        {
            Vector3 flat = worldPoint - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(flat.normalized, Vector3.up),
                yieldTurnSpeed * Time.deltaTime);
        }

        void Say(string message)
        {
            Debug.Log($"[RobotCleanerAI] {message}");
            onStatusMessage?.Invoke(message);
        }

        // ------------------------------------------------------------------ live re-scan

        void TickLiveRescan()
        {
            if (Time.time < nextRescanTime) return;
            nextRescanTime = Time.time + rescanInterval;

            int added = AddNewTargets();

            if (currentState == CleanerState.Completed)
            {
                // New litter appeared after we finished: go back to work
                if (added > 0)
                {
                    Say($"Found {added} new item(s). Resuming cleaning.");
                    SelectAndMoveToNextTarget();
                }
                return;
            }

            // The target was moved (e.g. a child picked it up and dropped it elsewhere): re-aim
            if (currentTarget && currentState != CleanerState.MovingToDropOff &&
                (currentTarget.transform.position - lastTargetPosition).sqrMagnitude > 0.01f)
            {
                lastTargetPosition = currentTarget.transform.position;
                agent.SetDestination(ComputeDestination(currentState));
            }
        }

        /// <summary>
        /// Called by UI/other scripts when the user answers the robot's question about an uncertain object.
        /// approve = true  -> the robot will pick it up and deliver it.
        /// approve = false -> the robot leaves it untouched.
        /// </summary>
        public void ProvideGuidance(GameObject target, bool approve)
        {
            if (!target) return;

            if (!approve)
            {
                skippedObjects.Add(target);
                Say($"Leaving '{target.name}' untouched.");
                return;
            }

            if (arm) arm.ApproveByUser(target);
            skippedObjects.Remove(target);
            if (!remainingTargets.Contains(target)) remainingTargets.Add(target);

            if (currentState == CleanerState.Completed || currentState == CleanerState.Idle)
            {
                SelectAndMoveToNextTarget();
            }
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

                // Fine-tune distance if slightly too far or too close for the scaled arm
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
                            // agent.Move keeps the robot on the NavMesh (unlike writing transform.position)
                            Vector3 next = Vector3.MoveTowards(transform.position, hit.position, moveSpeed * Time.deltaTime);
                            agent.Move(next - transform.position);
                            elapsed += Time.deltaTime;
                            yield return null;
                        }
                    }
                }

                yield return RotateTowardsPointRoutine(currentTarget.transform.position);
            }

            currentState = CleanerState.PickingUp;
            if (arm && currentTarget)
            {
                GameObject attempted = currentTarget;
                bool started = arm.PickUp(attempted, dropPoint: null);
                if (started)
                {
                    yield return new WaitUntil(() => !arm.IsBusy);
                }
                else
                {
                    // Leave it alone so the live re-scan does not queue it again every second
                    skippedObjects.Add(attempted);
                    if (!arm.ShouldPickUpWithoutGuidance(attempted))
                        Say($"Not sure about '{attempted.name}'. Waiting for your guidance.");
                    else
                        Say($"Could not reach '{attempted.name}'. Skipping it.");
                }
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
            else
            {
                SelectAndMoveToNextTarget();
            }
        }

        IEnumerator DropOffSequenceRoutine()
        {
            currentState = CleanerState.DroppingOff;

            if (dropOffLocation)
            {
                yield return RotateTowardsPointRoutine(dropOffLocation.position);
            }

            if (arm && arm.HeldObject != null)
            {
                GameObject droppedObj = arm.HeldObject;
                deliveredObjects.Add(droppedObj);

                // Place the item on the floor directly in front of the robot within the arm's reach
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

            if (vacuumPauseDuration > 0f)
                yield return new WaitForSeconds(vacuumPauseDuration);

            if (currentTarget)
            {
                Destroy(currentTarget);
                currentTarget = null;
            }

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
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    alignmentTurnSpeed * Time.deltaTime);
                yield return null;
            }
            transform.rotation = targetRotation;
        }

        Vector3 GetTargetApproachPosition(Transform target, float stopDistance)
        {
            Vector3 targetPos = target.position;
            Vector3 dirToRobot = transform.position - targetPos;
            dirToRobot.y = 0f;
            if (dirToRobot.sqrMagnitude < 0.001f)
                dirToRobot = -transform.forward;
            else
                dirToRobot.Normalize();

            Vector3 desiredStopPos = targetPos + dirToRobot * stopDistance;
            desiredStopPos.y = transform.position.y;
            return SampleNavMesh(desiredStopPos, 1.5f);
        }

        Vector3 GetDropOffApproachPosition(Transform dropTarget)
        {
            Vector3 dropPos = dropTarget.position;
            Collider dropCol = dropTarget.GetComponentInChildren<Collider>();
            if (dropCol)
            {
                // Find the closest point on the outside of the drop-off station collider towards the robot
                Vector3 surfacePoint = dropCol.ClosestPoint(transform.position);
                surfacePoint.y = transform.position.y;
                Vector3 dirOut = transform.position - dropCol.bounds.center;
                dirOut.y = 0f;
                if (dirOut.sqrMagnitude < 0.001f) dirOut = Vector3.forward;
                dirOut.Normalize();

                Vector3 desired = surfacePoint + dirOut * (WorldApproachDistance + 0.1f);
                return SampleNavMesh(desired, 2.0f);
            }

            return GetTargetApproachPosition(dropTarget, WorldApproachDistance);
        }

        Vector3 SampleNavMesh(Vector3 point, float maxDistance)
        {
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, maxDistance, NavMesh.AllAreas))
                return hit.position;
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
