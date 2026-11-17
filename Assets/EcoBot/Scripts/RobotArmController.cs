using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace EcoBot
{
    /// <summary>
    /// Animates the rigged arm of Robot_Vacuum_With_Arm_Rigged so it can pick up and place objects.
    ///
    /// Rig layout (all pivots start with identity rotation = rest pose, arm pointing up):
    ///   Arm_Yaw (turns around robot up)
    ///     Arm_Shoulder / Arm_Elbow / Arm_Wrist (hinges, bend in the robot's forward/up plane)
    ///       Finger_Left / Finger_Right (slide towards each other to grip)
    ///       GripPoint (point between the finger tips, where objects are held)
    ///
    /// The arm uses analytic inverse kinematics: yaw turns towards the object, shoulder + elbow
    /// solve a 2-link IK, and the wrist counter-rotates so the gripper always points down.
    ///
    /// Automatic Proximity / Trigger Pickup:
    ///   When autoPickupByTag is enabled, a proximity trigger zone and Physics.OverlapSphere sensor
    ///   automatically detect nearby objects tagged with pickupTag ("Pickupable"), pick the closest
    ///   reachable one up, and optionally place it at testDropPoint.
    /// </summary>
    [DisallowMultipleComponent]
    public class RobotArmController : MonoBehaviour
    {
        [Header("Rig pivots (auto-filled by name if empty)")]
        public Transform yaw;
        public Transform shoulder;
        public Transform elbow;
        public Transform wrist;
        public Transform fingerLeft;
        public Transform fingerRight;
        public Transform gripPoint;

        [Header("Gripper")]
        [Tooltip("Inner gap between the fingers when fully open (wrist-local units).")]
        public float restFingerGap = 0.195f;
        [Tooltip("Maximum distance each finger can slide inwards (wrist-local units).")]
        public float maxFingerTravel = 0.085f;
        [Tooltip("Seconds to open/close the gripper.")]
        public float gripperDuration = 0.35f;

        [Header("Motion")]
        [Tooltip("Seconds for a large arm move (e.g. rest -> above object).")]
        public float moveDuration = 1f;
        [Tooltip("How far above the object the gripper hovers before descending (world units).")]
        public float hoverHeight = 0.35f;
        [Tooltip("Offset added to the object's bounds centre when grabbing (world units).")]
        public Vector3 grabOffset = Vector3.zero;

        [Header("Automatic Proximity / Trigger Pickup")]
        [Tooltip("Automatically pick up nearby reachable objects with the specified tag.")]
        public bool autoPickupByTag = true;
        [Tooltip("Tag required on objects to be automatically picked up.")]
        public string pickupTag = "Pickupable";
        [Tooltip("Radius of the detection zone around the robot (local units).")]
        public float detectionRadius = 1.35f;
        [Tooltip("Center of the detection zone relative to the robot root (local units).")]
        public Vector3 detectionCenter = new Vector3(0f, 0.3f, 0.35f);
        [Tooltip("Cooldown in seconds before an object that was just dropped can be picked up again.")]
        public float rePickCooldown = 3f;

        [Header("Drop Point & Testing")]
        [Tooltip("Optional: where to put picked-up objects down (used by both auto-pickup and test target). Leave empty to just hold the object.")]
        public Transform testDropPoint;
        [Tooltip("Optional specific object to pick up automatically when Play starts (if Run Test On Start is on).")]
        public Transform testTarget;
        public bool runTestOnStart = true;

        [Header("Events")]
        public UnityEvent<GameObject> onPickedUp;
        public UnityEvent<GameObject> onReleased;

        public bool IsBusy { get; private set; }
        public GameObject HeldObject { get; private set; }

        struct ArmPose
        {
            public float yaw, shoulder, elbow, wrist; // degrees

            public static ArmPose Lerp(ArmPose a, ArmPose b, float t) => new ArmPose
            {
                yaw = Mathf.LerpAngle(a.yaw, b.yaw, t),
                shoulder = Mathf.LerpAngle(a.shoulder, b.shoulder, t),
                elbow = Mathf.LerpAngle(a.elbow, b.elbow, t),
                wrist = Mathf.LerpAngle(a.wrist, b.wrist, t),
            };
        }

        // Rest-pose data, captured in Awake.
        Quaternion yawRest, shoulderRest, elbowRest, wristRest;
        Vector3 shoulderAxis, elbowAxis, wristAxis; // hinge axis in each joint's local space
        Vector2 shoulderPlane;                      // shoulder pivot in (forward, up) plane of yaw space
        Vector2 wristToGrip;                        // grip point relative to wrist, in that plane
        float upperLen, foreLen, upperRestAngle, foreRestAngle, gripLateral;
        Vector3 fingerLeftRest, fingerRightRest, fingerLeftDir, fingerRightDir;

        ArmPose current;
        float fingerTravel;
        bool initialized;

        Transform heldOriginalParent;
        Rigidbody heldBody;
        bool heldWasKinematic;
        float heldBottomToGrip;

        // Proximity / Trigger detection state
        readonly HashSet<GameObject> triggerCandidates = new HashSet<GameObject>();
        readonly Dictionary<GameObject, float> ignoreUntilTime = new Dictionary<GameObject, float>();
        readonly Collider[] overlapBuffer = new Collider[32];
        SphereCollider proximityTrigger;

        // ------------------------------------------------------------------ setup

        void Reset() => AutoAssign();

        void Awake()
        {
            AutoAssign();
            Initialize();
            SetupProximitySensor();
        }

        void Start()
        {
            if (runTestOnStart && testTarget)
                StartCoroutine(RunTest());
        }

        void Update()
        {
            if (!initialized || !autoPickupByTag || IsBusy || HeldObject)
                return;

            TryAutoPickup();
        }

        IEnumerator RunTest()
        {
            yield return new WaitForSeconds(0.5f); // let physics settle
            if (testTarget && !IsBusy && !HeldObject)
                PickUp(testTarget.gameObject, testDropPoint);
        }

        void AutoAssign()
        {
            if (!yaw) yaw = FindDeep(transform, "Arm_Yaw");
            if (!shoulder) shoulder = FindDeep(transform, "Arm_Shoulder");
            if (!elbow) elbow = FindDeep(transform, "Arm_Elbow");
            if (!wrist) wrist = FindDeep(transform, "Arm_Wrist");
            if (!fingerLeft) fingerLeft = FindDeep(transform, "Finger_Left");
            if (!fingerRight) fingerRight = FindDeep(transform, "Finger_Right");
            if (!gripPoint) gripPoint = FindDeep(transform, "GripPoint");
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found) return found;
            }
            return null;
        }

        void SetupProximitySensor()
        {
            Transform sensorTransform = transform.Find("Pickup_Proximity_Sensor");
            GameObject sensorObj;
            if (sensorTransform)
            {
                sensorObj = sensorTransform.gameObject;
            }
            else
            {
                sensorObj = new GameObject("Pickup_Proximity_Sensor");
                sensorObj.transform.SetParent(transform, false);
            }

            sensorObj.layer = LayerMask.NameToLayer("Ignore Raycast");

            proximityTrigger = sensorObj.GetComponent<SphereCollider>();
            if (!proximityTrigger)
                proximityTrigger = sensorObj.AddComponent<SphereCollider>();
            proximityTrigger.isTrigger = true;
            proximityTrigger.center = detectionCenter;
            proximityTrigger.radius = detectionRadius;

            Rigidbody sensorRb = sensorObj.GetComponent<Rigidbody>();
            if (!sensorRb)
                sensorRb = sensorObj.AddComponent<Rigidbody>();
            sensorRb.isKinematic = true;
            sensorRb.useGravity = false;

            RobotArmProximitySensor relay = sensorObj.GetComponent<RobotArmProximitySensor>();
            if (!relay)
                relay = sensorObj.AddComponent<RobotArmProximitySensor>();
            relay.controller = this;
        }

        void Initialize()
        {
            if (!yaw || !shoulder || !elbow || !wrist || !fingerLeft || !fingerRight || !gripPoint)
            {
                Debug.LogError($"{name}: RobotArmController is missing rig references.", this);
                enabled = false;
                return;
            }

            yawRest = yaw.localRotation;
            shoulderRest = shoulder.localRotation;
            elbowRest = elbow.localRotation;
            wristRest = wrist.localRotation;

            // Arm plane in yaw space: forward = +Z, up = +Y, hinges turn around X.
            // Pick the hinge direction so that a positive angle bends forward -> up.
            Vector3 hinge = Vector3.Dot(Quaternion.AngleAxis(90f, Vector3.right) * Vector3.forward, Vector3.up) > 0f
                ? Vector3.right
                : Vector3.left;
            shoulderAxis = HingeInJointSpace(shoulder, hinge);
            elbowAxis = HingeInJointSpace(elbow, hinge);
            wristAxis = HingeInJointSpace(wrist, hinge);

            Vector3 s = yaw.InverseTransformPoint(shoulder.position);
            Vector3 e = yaw.InverseTransformPoint(elbow.position);
            Vector3 w = yaw.InverseTransformPoint(wrist.position);
            Vector3 g = yaw.InverseTransformPoint(gripPoint.position);

            shoulderPlane = Plane(s);
            Vector2 upper = Plane(e) - Plane(s);
            Vector2 fore = Plane(w) - Plane(e);
            upperLen = upper.magnitude;
            foreLen = fore.magnitude;
            upperRestAngle = Mathf.Atan2(upper.y, upper.x);
            foreRestAngle = Mathf.Atan2(fore.y, fore.x);
            wristToGrip = Plane(g) - Plane(w);
            gripLateral = g.x; // sideways offset of the grip point from the yaw axis

            // Fingers slide along the line between them.
            Vector3 worldAcross = fingerRight.position - fingerLeft.position;
            fingerLeftRest = fingerLeft.localPosition;
            fingerRightRest = fingerRight.localPosition;
            fingerLeftDir = fingerLeft.parent.InverseTransformDirection(worldAcross).normalized;
            fingerRightDir = fingerRight.parent.InverseTransformDirection(-worldAcross).normalized;

            current = default;
            fingerTravel = 0f;
            initialized = true;
        }

        Vector3 HingeInJointSpace(Transform joint, Vector3 hingeInYawSpace)
        {
            Quaternion jointInYaw = Quaternion.Inverse(yaw.rotation) * joint.rotation;
            return Quaternion.Inverse(jointInYaw) * hingeInYawSpace;
        }

        static Vector2 Plane(Vector3 yawLocal) => new Vector2(yawLocal.z, yawLocal.y);

        // ------------------------------------------------------------------ proximity & trigger detection

        internal void NotifyTriggerEnter(Collider other)
        {
            GameObject candidate = ResolvePickupCandidate(other);
            if (candidate)
                triggerCandidates.Add(candidate);
        }

        internal void NotifyTriggerExit(Collider other)
        {
            GameObject candidate = ResolvePickupCandidate(other);
            if (candidate)
                triggerCandidates.Remove(candidate);
        }

        GameObject ResolvePickupCandidate(Collider col)
        {
            if (!col || col.transform.IsChildOf(transform))
                return null;

            if (col.attachedRigidbody && HasPickupTag(col.attachedRigidbody.gameObject))
                return col.attachedRigidbody.gameObject;

            Transform t = col.transform;
            while (t != null)
            {
                if (HasPickupTag(t.gameObject))
                    return t.gameObject;
                t = t.parent;
            }

            return null;
        }

        bool HasPickupTag(GameObject go)
        {
            if (string.IsNullOrEmpty(pickupTag))
                return false;
            try
            {
                return go.CompareTag(pickupTag);
            }
            catch (UnityException)
            {
                return false;
            }
        }

        bool IsCandidateValidAndReachable(GameObject candidate, out float sqrDist)
        {
            sqrDist = float.MaxValue;
            if (!candidate || !candidate.activeInHierarchy || candidate == HeldObject)
                return false;

            if (candidate.transform.IsChildOf(transform))
                return false;

            if (ignoreUntilTime.TryGetValue(candidate, out float cooldownEnd))
            {
                if (Time.time < cooldownEnd)
                    return false;
                ignoreUntilTime.Remove(candidate);
            }

            if (!HasPickupTag(candidate))
                return false;

            // Don't grab an item that was just dropped right onto the drop point
            if (testDropPoint && Vector3.Distance(candidate.transform.position, testDropPoint.position) < 0.2f)
                return false;

            Vector3 grab = GrabPointFor(candidate);
            if (!Solve(grab, out _) || !Solve(grab + transform.up * hoverHeight, out _))
                return false;

            sqrDist = (grab - transform.position).sqrMagnitude;
            return true;
        }

        void TryAutoPickup()
        {
            if (proximityTrigger)
            {
                proximityTrigger.center = detectionCenter;
                proximityTrigger.radius = detectionRadius;
            }

            GameObject bestTarget = null;
            float bestSqrDist = float.MaxValue;

            // 1) Check candidates registered by OnTriggerEnter
            triggerCandidates.RemoveWhere(item => item == null);
            foreach (GameObject candidate in triggerCandidates)
            {
                if (IsCandidateValidAndReachable(candidate, out float sqrDist) && sqrDist < bestSqrDist)
                {
                    bestSqrDist = sqrDist;
                    bestTarget = candidate;
                }
            }

            // 2) Also scan via OverlapSphere so objects without a Rigidbody or already inside at Start are caught
            Vector3 worldSensorCenter = transform.TransformPoint(detectionCenter);
            float worldScale = Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z));
            int count = Physics.OverlapSphereNonAlloc(worldSensorCenter, detectionRadius * worldScale, overlapBuffer, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                GameObject candidate = ResolvePickupCandidate(overlapBuffer[i]);
                if (candidate && IsCandidateValidAndReachable(candidate, out float sqrDist) && sqrDist < bestSqrDist)
                {
                    bestSqrDist = sqrDist;
                    bestTarget = candidate;
                }
            }

            if (bestTarget)
                PickUp(bestTarget, testDropPoint);
        }

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// TODO: AI decision function to determine if the robot is confident to pick up 'target'.
        /// Returns true if confident to pick up immediately.
        /// Returns false if uncertain, which triggers asking the user for guidance.
        /// </summary>
        /// <param name="target">The candidate GameObject to be evaluated.</param>
        /// <returns>True to proceed with pickup, false to ask user for guidance.</returns>
        public bool ShouldPickUpWithoutGuidance(GameObject target)
        {
            // TODO: Use AI to evaluate whether to pick up 'target' without human intervention.
            // Returning true means confident -> pick up automatically.
            // Returning false means uncertain -> prompt user for guidance.
            return true;
        }

        /// <summary>
        /// TODO: Handle prompting the user for guidance when ShouldPickUpWithoutGuidance returns false.
        /// </summary>
        /// <param name="target">The candidate GameObject requiring human clarification.</param>
        public void AskUserForGuidance(GameObject target)
        {
            // TODO: Trigger UI / audio / confirmation prompt asking the user for guidance.
        }

        /// <summary>Pick up an object and hold it (or place it at dropPoint if given; defaults to testDropPoint if null).</summary>
        public bool PickUp(GameObject target, Transform dropPoint = null)
        {
            if (!initialized || !target) return false;
            if (IsBusy) { Debug.LogWarning($"{name}: arm is busy.", this); return false; }
            if (HeldObject) { Debug.LogWarning($"{name}: already holding {HeldObject.name}.", this); return false; }

            // AI Decision: decide whether to proceed autonomously or ask the user for guidance
            if (!ShouldPickUpWithoutGuidance(target))
            {
                AskUserForGuidance(target);
                return false;
            }

            if (!dropPoint)
                dropPoint = testDropPoint;

            Vector3 grab = GrabPointFor(target);
            if (!Solve(grab, out _) || !Solve(grab + transform.up * hoverHeight, out _))
            {
                Debug.LogWarning($"{name}: {target.name} is out of the arm's reach. " +
                                 "Move the robot closer (objects should be in front of it, just past the bumper).", this);
                return false;
            }

            StartCoroutine(PickRoutine(target, grab, dropPoint));
            return true;
        }

        public bool PickUp(Transform target, Transform dropPoint = null) =>
            target && PickUp(target.gameObject, dropPoint);

        /// <summary>Put the held object down so its bottom rests at worldPosition.</summary>
        public bool PlaceAt(Vector3 worldPosition)
        {
            if (!initialized || IsBusy || !HeldObject) return false;
            StartCoroutine(Run(PlaceRoutine(worldPosition)));
            return true;
        }

        /// <summary>Let go of the held object immediately and return to rest.</summary>
        public void Release()
        {
            if (!HeldObject || IsBusy) return;
            Detach();
            StartCoroutine(Run(OpenAndRest()));
        }

        /// <summary>Move the arm back to its upright rest pose.</summary>
        public void ReturnToRest()
        {
            if (initialized && !IsBusy) StartCoroutine(Run(MoveToPose(default, moveDuration)));
        }

        /// <summary>True if the arm can reach this world point with the gripper pointing down.</summary>
        public bool CanReach(Vector3 worldPoint) => initialized && Solve(worldPoint, out _);

        [ContextMenu("Test: Pick Up Test Target")]
        void ContextPick() { if (testTarget) PickUp(testTarget.gameObject, testDropPoint); }

        [ContextMenu("Test: Release")]
        void ContextRelease() => Release();

        // ------------------------------------------------------------------ sequences

        IEnumerator Run(IEnumerator routine)
        {
            IsBusy = true;
            yield return routine;
            IsBusy = false;
        }

        IEnumerator PickRoutine(GameObject target, Vector3 grab, Transform dropPoint)
        {
            IsBusy = true;
            Vector3 up = transform.up;
            Vector3 hover = grab + up * hoverHeight;

            yield return AnimateGrip(0f, gripperDuration * 0.5f);       // make sure fingers are open
            Solve(hover, out ArmPose hoverPose);
            yield return MoveToPose(hoverPose, moveDuration);          // swing over the object
            yield return MoveLinear(hover, grab, moveDuration * 0.6f); // lower straight down

            if (target)
            {
                yield return AnimateGrip(TravelFor(target), gripperDuration); // close fingers
                Attach(target);
                yield return MoveLinear(grab, hover, moveDuration * 0.6f);     // lift
            }

            if (HeldObject && dropPoint)
                yield return PlaceRoutine(dropPoint.position);
            else
                yield return MoveToPose(default, moveDuration);        // carry in rest pose

            IsBusy = false;
        }

        IEnumerator PlaceRoutine(Vector3 worldPosition)
        {
            Vector3 up = transform.up;
            Vector3 place = worldPosition + up * (heldBottomToGrip + 0.01f);
            Vector3 hover = place + up * hoverHeight;

            if (!Solve(place, out _))
                Debug.LogWarning($"{name}: drop point is out of reach, placing as close as possible.", this);

            Solve(hover, out ArmPose hoverPose);
            yield return MoveToPose(hoverPose, moveDuration);
            yield return MoveLinear(hover, place, moveDuration * 0.6f);
            Detach();
            yield return AnimateGrip(0f, gripperDuration);
            yield return MoveLinear(place, hover, moveDuration * 0.6f);
            yield return MoveToPose(default, moveDuration);
        }

        IEnumerator OpenAndRest()
        {
            yield return AnimateGrip(0f, gripperDuration);
            yield return MoveToPose(default, moveDuration);
        }

        // ------------------------------------------------------------------ motion primitives

        IEnumerator MoveToPose(ArmPose target, float duration)
        {
            ArmPose start = current;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                Apply(ArmPose.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / duration)));
                yield return null;
            }
            Apply(target);
        }

        IEnumerator MoveLinear(Vector3 from, Vector3 to, float duration)
        {
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                Solve(Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration)), out ArmPose pose);
                Apply(pose);
                yield return null;
            }
            Solve(to, out ArmPose end);
            Apply(end);
        }

        IEnumerator AnimateGrip(float targetTravel, float duration)
        {
            float start = fingerTravel;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                SetFingers(Mathf.Lerp(start, targetTravel, Mathf.SmoothStep(0f, 1f, t / duration)));
                yield return null;
            }
            SetFingers(targetTravel);
        }

        void SetFingers(float travel)
        {
            fingerTravel = travel;
            fingerLeft.localPosition = fingerLeftRest + fingerLeftDir * travel;
            fingerRight.localPosition = fingerRightRest + fingerRightDir * travel;
        }

        void Apply(ArmPose pose)
        {
            current = pose;
            yaw.localRotation = yawRest * Quaternion.AngleAxis(pose.yaw, Vector3.up);
            shoulder.localRotation = shoulderRest * Quaternion.AngleAxis(pose.shoulder, shoulderAxis);
            elbow.localRotation = elbowRest * Quaternion.AngleAxis(pose.elbow, elbowAxis);
            wrist.localRotation = wristRest * Quaternion.AngleAxis(pose.wrist, wristAxis);
        }

        // ------------------------------------------------------------------ inverse kinematics

        /// <summary>
        /// Computes joint angles that put GripPoint at worldGrip with the gripper pointing down.
        /// Returns false if the point is out of reach (the pose is then the closest possible).
        /// </summary>
        bool Solve(Vector3 worldGrip, out ArmPose pose)
        {
            bool reachable = true;

            // Target in the yaw pivot's rest frame.
            Quaternion saved = yaw.localRotation;
            yaw.localRotation = yawRest;
            Vector3 p = yaw.InverseTransformPoint(worldGrip);
            yaw.localRotation = saved;

            // 1) Yaw: turn so the (sideways-offset) grip point lies over the target.
            float horizontalSq = p.x * p.x + p.z * p.z;
            float forward;
            if (horizontalSq <= gripLateral * gripLateral + 1e-6f)
            {
                reachable = false;
                forward = 0.001f;
            }
            else forward = Mathf.Sqrt(horizontalSq - gripLateral * gripLateral);

            float yawAngle = Vector3.SignedAngle(new Vector3(gripLateral, 0f, forward),
                                                 new Vector3(p.x, 0f, p.z), Vector3.up);

            // 2) Shoulder + elbow: 2-link IK to put the wrist where the gripper can reach the target.
            Vector2 wristGoal = new Vector2(forward, p.y) - wristToGrip;
            Vector2 toWrist = wristGoal - shoulderPlane;
            float dist = toWrist.magnitude;
            float minDist = Mathf.Abs(upperLen - foreLen) + 1e-4f;
            float maxDist = upperLen + foreLen - 1e-4f;
            if (dist < minDist || dist > maxDist) reachable = false;
            dist = Mathf.Clamp(dist, minDist, maxDist);
            Vector2 dir = toWrist.sqrMagnitude > 1e-8f ? toWrist.normalized : Vector2.right;

            float cosInner = Mathf.Clamp((upperLen * upperLen + dist * dist - foreLen * foreLen) /
                                         (2f * upperLen * dist), -1f, 1f);
            float upperAngle = Mathf.Atan2(dir.y, dir.x) + Mathf.Acos(cosInner); // elbow up
            Vector2 elbowPos = new Vector2(Mathf.Cos(upperAngle), Mathf.Sin(upperAngle)) * upperLen;
            Vector2 wristPos = dir * dist;
            float foreAngle = Mathf.Atan2(wristPos.y - elbowPos.y, wristPos.x - elbowPos.x);

            float shoulderDelta = Mathf.DeltaAngle(0f, (upperAngle - upperRestAngle) * Mathf.Rad2Deg);
            float foreDelta = Mathf.DeltaAngle(0f, (foreAngle - foreRestAngle) * Mathf.Rad2Deg);

            pose = new ArmPose
            {
                yaw = yawAngle,
                shoulder = shoulderDelta,
                elbow = Mathf.DeltaAngle(0f, foreDelta - shoulderDelta),
                wrist = -foreDelta, // 3) keep gripper orientation = rest (fingers pointing down)
            };
            return reachable;
        }

        // ------------------------------------------------------------------ holding objects

        Vector3 GrabPointFor(GameObject target) => GetBounds(target).center + grabOffset;

        float TravelFor(GameObject target)
        {
            Bounds b = GetBounds(target);
            Vector3 across = (fingerRight.position - fingerLeft.position).normalized;
            float worldWidth = 2f * (Mathf.Abs(b.extents.x * across.x) +
                                     Mathf.Abs(b.extents.y * across.y) +
                                     Mathf.Abs(b.extents.z * across.z));
            float localPerWorld = 1f / Mathf.Max(1e-6f, fingerLeft.parent.TransformVector(fingerLeftDir).magnitude);
            float localWidth = worldWidth * localPerWorld;

            if (localWidth > restFingerGap)
                Debug.LogWarning($"{name}: {target.name} is wider than the open gripper; holding it anyway.", this);

            return Mathf.Clamp((restFingerGap - localWidth) * 0.5f, 0f, maxFingerTravel);
        }

        static Bounds GetBounds(GameObject go)
        {
            bool has = false;
            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            if (has) return b;
            foreach (Collider c in go.GetComponentsInChildren<Collider>())
            {
                if (!has) { b = c.bounds; has = true; } else b.Encapsulate(c.bounds);
            }
            return b;
        }

        void Attach(GameObject target)
        {
            heldBody = target.GetComponent<Rigidbody>();
            if (heldBody)
            {
                heldWasKinematic = heldBody.isKinematic;
                if (!heldBody.isKinematic)
                {
                    heldBody.linearVelocity = Vector3.zero;
                    heldBody.angularVelocity = Vector3.zero;
                }
                heldBody.isKinematic = true;
            }

            heldOriginalParent = target.transform.parent;
            target.transform.SetParent(gripPoint, true);
            heldBottomToGrip = Vector3.Dot(gripPoint.position - GetBounds(target).min, transform.up);
            heldBottomToGrip = Mathf.Max(0f, heldBottomToGrip);

            HeldObject = target;
            onPickedUp?.Invoke(target);
        }

        void Detach()
        {
            GameObject obj = HeldObject;
            if (!obj) return;

            obj.transform.SetParent(heldOriginalParent, true);
            if (heldBody) heldBody.isKinematic = heldWasKinematic;

            if (rePickCooldown > 0f)
                ignoreUntilTime[obj] = Time.time + rePickCooldown;

            HeldObject = null;
            heldBody = null;
            onReleased?.Invoke(obj);
        }

        void OnDrawGizmosSelected()
        {
            if (!autoPickupByTag) return;
            Gizmos.color = new Color(0.1f, 0.85f, 0.35f, 0.35f);
            Vector3 worldCenter = transform.TransformPoint(detectionCenter);
            float worldScale = Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z));
            Gizmos.DrawWireSphere(worldCenter, detectionRadius * worldScale);
        }
    }

    /// <summary>
    /// Helper trigger component created automatically on the robot's Pickup_Proximity_Sensor child.
    /// Relays OnTriggerEnter / OnTriggerExit events to RobotArmController.
    /// </summary>
    [AddComponentMenu("")]
    public class RobotArmProximitySensor : MonoBehaviour
    {
        [HideInInspector] public RobotArmController controller;

        void OnTriggerEnter(Collider other)
        {
            if (controller) controller.NotifyTriggerEnter(other);
        }

        void OnTriggerStay(Collider other)
        {
            if (controller) controller.NotifyTriggerEnter(other);
        }

        void OnTriggerExit(Collider other)
        {
            if (controller) controller.NotifyTriggerExit(other);
        }
    }
}
