using UnityEngine;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class Pickupable : MonoBehaviour
{
    void Reset()
    {
        gameObject.tag = "Pickupable";
        GetComponent<Collider>().isTrigger = false;
        Rigidbody rb = GetComponent<Rigidbody>();
        rb.mass = 0.2f;
    }
}
