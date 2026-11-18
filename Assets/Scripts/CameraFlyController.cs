using UnityEngine;

public class CameraFlyController : MonoBehaviour
{
    public float panSpeed = 10f;
    public float rotSpeed = 3f;
    public Transform lookTarget;

    void Update()
    {
        // Simple rotation around scene center on left/right arrow or A/D
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        if (h != 0 || v != 0)
        {
            transform.Translate(new Vector3(h, 0, v) * panSpeed * Time.deltaTime, Space.Self);
        }

        if (Input.GetMouseButton(1))
        {
            float mouseX = Input.GetAxis("Mouse X") * rotSpeed;
            float mouseY = -Input.GetAxis("Mouse Y") * rotSpeed;
            transform.eulerAngles += new Vector3(mouseY, mouseX, 0);
        }
    }
}

