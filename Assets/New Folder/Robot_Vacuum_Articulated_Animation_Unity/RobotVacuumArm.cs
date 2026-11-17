using System.Collections;
using UnityEngine;

/// <summary>
/// Procedurally builds a visible 2-link robot arm and animates it with planar IK.
/// Add this component to the vacuum body. No Animator Controller is required.
/// </summary>
public class RobotVacuumArm : MonoBehaviour
{
    [Header("Mount and dimensions (Unity units)")]
    public Vector3 shoulderLocalPosition = new Vector3(0f, 0.49f, -0.02f);
    public float upperLength = 0.78f;
    public float forearmLength = 0.72f;
    public float linkWidth = 0.15f;
    public float linkDepth = 0.13f;
    public float jointRadius = 0.12f;

    [Header("Pickup animation")]
    public float moveDuration = 0.65f;
    public float gripDuration = 0.22f;
    public float openGap = 0.15f;
    public float closedGap = 0.045f;
    public float approachHeight = 0.18f;
    public float liftHeight = 0.55f;
    public string pickupTag = "Pickupable";

    public GameObject HeldObject { get; private set; }

    Transform armRoot, shoulderPivot, elbowPivot, wristPivot;
    Transform upperLink, forearmLink, gripper, leftFinger, rightFinger, holdPoint;
    bool busy;

    void Start()
    {
        BuildArm();
    }

    [ContextMenu("Rebuild Articulated Arm")]
    public void BuildArm()
    {
        Transform old = transform.Find("ArticulatedArm");
        if (old) Destroy(old.gameObject);

        armRoot = new GameObject("ArticulatedArm").transform;
        armRoot.SetParent(transform, false);
        armRoot.localPosition = shoulderLocalPosition;
        armRoot.localRotation = Quaternion.identity;
        armRoot.localScale = Vector3.one;

        shoulderPivot = MakePivot("Shoulder_Pivot", armRoot, Vector3.zero);
        MakeJointVisual("Shoulder_Joint_Visual", shoulderPivot, jointRadius);
        upperLink = MakeLink("Upper_Arm_Link", shoulderPivot, upperLength, linkWidth, linkDepth);

        elbowPivot = MakePivot("Elbow_Pivot", shoulderPivot, new Vector3(upperLength, 0, 0));
        MakeJointVisual("Elbow_Joint_Visual", elbowPivot, jointRadius * 0.88f);
        forearmLink = MakeLink("Forearm_Link", elbowPivot, forearmLength, linkWidth * 0.9f, linkDepth * 0.9f);

        wristPivot = MakePivot("Wrist_Pivot", elbowPivot, new Vector3(forearmLength, 0, 0));
        MakeJointVisual("Wrist_Joint_Visual", wristPivot, jointRadius * 0.68f);

        gripper = new GameObject("Two_Finger_Gripper").transform;
        gripper.SetParent(wristPivot, false);
        gripper.localPosition = new Vector3(0.08f, 0, 0);
        // Palm and fingers are oriented across the arm plane (local Y).
        MakeCube("Gripper_Palm", gripper, Vector3.zero,
            new Vector3(0.20f, 0.22f, 0.14f), new Color(0.30f,0.33f,0.36f));
        leftFinger = MakeCube("Left_Gripper_Finger", gripper,
            new Vector3(0.08f, openGap, 0), new Vector3(0.22f,0.045f,0.07f),
            new Color(0.58f,0.60f,0.62f));
        rightFinger = MakeCube("Right_Gripper_Finger", gripper,
            new Vector3(0.08f, -openGap, 0), new Vector3(0.22f,0.045f,0.07f),
            new Color(0.58f,0.60f,0.62f));

        holdPoint = new GameObject("Object_Hold_Point").transform;
        holdPoint.SetParent(gripper, false);
        holdPoint.localPosition = new Vector3(0.23f, 0, 0);

        BoxCollider trigger = gripper.gameObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(0.32f, 0.30f, 0.25f);
        Rigidbody gripperBody = gripper.gameObject.AddComponent<Rigidbody>();
        gripperBody.isKinematic = true;
        gripperBody.useGravity = false;
        PickupTrigger pickupTrigger = gripper.gameObject.AddComponent<PickupTrigger>();
        pickupTrigger.arm = this;

        // Start in an upright, folded pose similar to the reference image.
        SetArmToTarget(armRoot.TransformPoint(new Vector3(0.12f, 1.28f, 0f)));
        SetFingerGap(openGap);
    }

    Transform MakePivot(string name, Transform parent, Vector3 localPosition)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPosition;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one; // Never scale pivots: child links retain their dimensions.
        return t;
    }

    void MakeJointVisual(string name, Transform parent, float radius)
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = name;
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.Euler(90,0,0);
        visual.transform.localScale = new Vector3(radius * 1.6f, 0.055f, radius * 1.6f);
        SetMaterial(visual, new Color(0.13f,0.15f,0.17f), 0.65f);
        RemoveCollider(visual);
        // Add circular side caps for the visible hinge appearance.
        GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cap.name = name + "_Cap";
        cap.transform.SetParent(parent, false);
        cap.transform.localPosition = new Vector3(0,0,-0.065f);
        cap.transform.localRotation = Quaternion.Euler(90,0,0);
        cap.transform.localScale = new Vector3(radius,0.035f,radius);
        SetMaterial(cap, new Color(0.34f,0.36f,0.38f), 0.7f);
        RemoveCollider(cap);
    }

    Transform MakeLink(string name, Transform parent, float length, float width, float depth)
    {
        GameObject link = GameObject.CreatePrimitive(PrimitiveType.Cube);
        link.name = name;
        link.transform.SetParent(parent, false);
        link.transform.localPosition = new Vector3(length * 0.5f,0,0);
        link.transform.localRotation = Quaternion.identity;
        link.transform.localScale = new Vector3(length,width,depth);
        SetMaterial(link, new Color(0.055f,0.065f,0.075f), 0.72f);
        RemoveCollider(link);
        return link.transform;
    }

    Transform MakeCube(string name, Transform parent, Vector3 localPos, Vector3 scale, Color color)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localScale = scale;
        SetMaterial(obj,color,0.55f);
        RemoveCollider(obj);
        return obj.transform;
    }

    void SetMaterial(GameObject obj, Color color, float metallic)
    {
        Renderer r = obj.GetComponent<Renderer>();
        if (!r) return;
        Shader shader = Shader.Find("Standard");
        if (!shader) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) shader = Shader.Find("HDRP/Lit");
        if (shader)
        {
            Material mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic",metallic);
            r.material = mat;
        }
    }

    void RemoveCollider(GameObject obj)
    {
        Collider c = obj.GetComponent<Collider>();
        if (c) Destroy(c);
    }

    void SetFingerGap(float gap)
    {
        if (!leftFinger || !rightFinger) return;
        Vector3 l = leftFinger.localPosition; l.y = gap; leftFinger.localPosition = l;
        Vector3 r = rightFinger.localPosition; r.y = -gap; rightFinger.localPosition = r;
    }

    // Planar two-link inverse kinematics. Links extend along local +X; joint rotation is around local Z.
    void SetArmToTarget(Vector3 worldTarget)
    {
        Vector3 local = armRoot.InverseTransformPoint(worldTarget);
        float x = local.x;
        float y = local.y;
        float d = Mathf.Sqrt(x*x + y*y);
        float minD = Mathf.Abs(upperLength - forearmLength) + 0.001f;
        float maxD = upperLength + forearmLength - 0.001f;
        d = Mathf.Clamp(d,minD,maxD);

        float cosElbow = Mathf.Clamp((d*d - upperLength*upperLength - forearmLength*forearmLength) /
            (2f*upperLength*forearmLength),-1f,1f);
        float elbowAngle = Mathf.Acos(cosElbow); // elbow bends toward the floor
        float shoulderAngle = Mathf.Atan2(y,x) -
            Mathf.Atan2(forearmLength*Mathf.Sin(elbowAngle),
                        upperLength + forearmLength*Mathf.Cos(elbowAngle));

        shoulderPivot.localRotation = Quaternion.Euler(0,0,shoulderAngle*Mathf.Rad2Deg);
        elbowPivot.localRotation = Quaternion.Euler(0,0,elbowAngle*Mathf.Rad2Deg);
        wristPivot.localRotation = Quaternion.identity;
    }

    public void ReachAndPick(GameObject target)
    {
        if (!busy && target) StartCoroutine(PickRoutine(target));
    }

    IEnumerator PickRoutine(GameObject target)
    {
        busy = true;
        Vector3 objectPos = target.transform.position;
        Vector3 above = objectPos + Vector3.up * approachHeight;
        yield return MoveEndEffector(above, moveDuration);
        yield return AnimateGrip(closedGap,gripDuration);

        if (target)
        {
            Rigidbody targetRb = target.GetComponent<Rigidbody>();
            if (targetRb)
            {
                targetRb.linearVelocity = Vector3.zero;
                targetRb.angularVelocity = Vector3.zero;
                targetRb.isKinematic = true;
            }
            target.transform.SetParent(holdPoint,true);
            target.transform.position = holdPoint.position;
            target.transform.rotation = holdPoint.rotation;
            HeldObject = target;
        }

        yield return MoveEndEffector(above + Vector3.up*liftHeight,moveDuration);
        busy = false;
    }

    IEnumerator MoveEndEffector(Vector3 target, float duration)
    {
        Vector3 start = wristPivot.position + wristPivot.right * 0.08f;
        float t=0;
        while(t<duration)
        {
            t += Time.deltaTime;
            Vector3 p = Vector3.Lerp(start,target,Mathf.SmoothStep(0,1,t/duration));
            SetArmToTarget(p);
            yield return null;
        }
        SetArmToTarget(target);
    }

    public void Drop()
    {
        if (!HeldObject) return;
        GameObject item = HeldObject;
        HeldObject = null;
        item.transform.SetParent(null,true);
        Rigidbody rb = item.GetComponent<Rigidbody>();
        if (rb) rb.isKinematic = false;
        StartCoroutine(AnimateGrip(openGap,gripDuration));
        StartCoroutine(ReturnToRest());
    }

    IEnumerator ReturnToRest()
    {
        Vector3 rest = armRoot.TransformPoint(new Vector3(0.12f,1.28f,0));
        yield return MoveEndEffector(rest,moveDuration);
    }

    IEnumerator AnimateGrip(float targetGap,float duration)
    {
        Vector3 l0=leftFinger.localPosition, r0=rightFinger.localPosition;
        float t=0;
        while(t<duration)
        {
            t+=Time.deltaTime;
            float a=Mathf.SmoothStep(0,1,t/duration);
            Vector3 l=l0; l.y=Mathf.Lerp(l0.y,targetGap,a); leftFinger.localPosition=l;
            Vector3 r=r0; r.y=Mathf.Lerp(r0.y,-targetGap,a); rightFinger.localPosition=r;
            yield return null;
        }
        SetFingerGap(targetGap);
    }
}
