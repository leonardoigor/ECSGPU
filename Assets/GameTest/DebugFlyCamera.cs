using UnityEngine;

public sealed class DebugFlyCamera : MonoBehaviour
{
    public float moveSpeed = 12f;
    public float fastMoveMultiplier = 3f;
    public float lookSensitivity = 2.5f;

    float yaw;
    float pitch;

    void Awake()
    {
        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = euler.x;
        LockCursor();
    }

    void Update()
    {
        UpdateLook();
        UpdateMovement();
    }

    void UpdateLook()
    {
        if (Cursor.lockState == CursorLockMode.None && Input.GetMouseButtonDown(0))
            LockCursor();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        yaw += Input.GetAxisRaw("Mouse X") * lookSensitivity;
        pitch -= Input.GetAxisRaw("Mouse Y") * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -89f, 89f);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void UpdateMovement()
    {
        float speed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift))
            speed *= fastMoveMultiplier;

        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move += transform.forward;
        if (Input.GetKey(KeyCode.S)) move -= transform.forward;
        if (Input.GetKey(KeyCode.D)) move += transform.right;
        if (Input.GetKey(KeyCode.A)) move -= transform.right;
        if (Input.GetKey(KeyCode.E)) move += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;

        if (move.sqrMagnitude > 1f)
            move.Normalize();

        transform.position += move * speed * Time.deltaTime;
    }
}
