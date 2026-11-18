using UnityEngine;
using UnityEngine.EventSystems;
using EcoBot;

/// <summary>
/// Lets the human player drag any Pickupables-layer object during Play.
/// Auto-attaches itself to every object on the Pickupables layer on scene load.
/// On release, notifies all RobotCleanerAI instances so they can retry picking it up
/// from the new location (handles the case where the robot had given up on it).
/// </summary>
[DisallowMultipleComponent]
public class DraggableItem : MonoBehaviour
{
    [Tooltip("Optional camera override. Defaults to Camera.main.")]
    public Camera targetCamera;

    private bool dragging;
    private Vector3 dragOffset;
    private Plane dragPlane;
    private Rigidbody rb;
    private bool rbWasKinematic;
    private RobotCleanerAI[] robots;
    private RobotArmController arm;

    void Awake() => rb = GetComponent<Rigidbody>();

    void Start()
    {
        robots = FindObjectsByType<RobotCleanerAI>(FindObjectsSortMode.None);
        if (robots.Length > 0) arm = robots[0].GetComponent<RobotArmController>();
    }

    void Update()
    {
        if (Camera.main == null) return;

        if (!dragging)
        {
            if (MouseDown(0)) TryBeginDrag();
        }
        else
        {
            if (MouseHeld(0)) UpdateDrag();
            else EndDrag();
        }
    }

    void TryBeginDrag()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (arm != null && arm.HeldObject == gameObject) return; // robot is carrying it

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        Ray ray = cam.ScreenPointToRay(MousePos());
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, ~0, QueryTriggerInteraction.Ignore)) return;

        if (hit.collider.gameObject != gameObject && !hit.collider.transform.IsChildOf(transform)) return;

        dragging = true;
        dragPlane = new Plane(Vector3.up, transform.position);
        dragOffset = hit.point - transform.position;

        if (rb != null) { rbWasKinematic = rb.isKinematic; rb.isKinematic = true; }
    }

    void UpdateDrag()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        Ray ray = cam.ScreenPointToRay(MousePos());
        if (dragPlane.Raycast(ray, out float dist))
        {
            Vector3 target = ray.GetPoint(dist) - dragOffset;
            target.y = transform.position.y; // slide along the floor
            transform.position = target;
        }
    }

    void EndDrag()
    {
        dragging = false;
        if (rb != null) rb.isKinematic = rbWasKinematic;

        if (robots == null || robots.Length == 0)
            robots = FindObjectsByType<RobotCleanerAI>(FindObjectsSortMode.None);

        foreach (var ai in robots)
            if (ai != null) ai.NotifyObjectMoved(gameObject);
    }

    // ---- input helpers, work with legacy and new Input System ----

    static bool MouseDown(int button)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(button)) return true;
#endif
#if ENABLE_INPUT_SYSTEM
        var m = UnityEngine.InputSystem.Mouse.current;
        if (m != null)
        {
            if (button == 0) return m.leftButton.wasPressedThisFrame;
            if (button == 1) return m.rightButton.wasPressedThisFrame;
        }
#endif
        return false;
    }

    static bool MouseHeld(int button)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButton(button)) return true;
#endif
#if ENABLE_INPUT_SYSTEM
        var m = UnityEngine.InputSystem.Mouse.current;
        if (m != null)
        {
            if (button == 0) return m.leftButton.isPressed;
            if (button == 1) return m.rightButton.isPressed;
        }
#endif
        return false;
    }

    static Vector3 MousePos()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mousePosition;
#elif ENABLE_INPUT_SYSTEM
        var m = UnityEngine.InputSystem.Mouse.current;
        return m != null ? (Vector3)m.position.ReadValue() : Vector3.zero;
#else
        return Vector3.zero;
#endif
    }

    // Auto-attach to every object already on the Pickupables layer when the scene loads.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttachOnPlay()
    {
        int layer = LayerMask.NameToLayer("Pickupables");
        if (layer < 0) return;

        foreach (var col in FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (col == null || col.gameObject.layer != layer) continue;

            GameObject root = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.gameObject;
            if (root.GetComponent<DraggableItem>() == null && root.GetComponentInParent<DraggableItem>() == null)
                root.AddComponent<DraggableItem>();
        }
    }
}