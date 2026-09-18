using UnityEngine;
using UnityEngine.InputSystem;

public class CameraManager : MonoBehaviour
{
    [Header("Movimiento")]
    public float moveSpeed = 50f;

    [Header("Zoom")]
    public float zoomSpeed = 50f;
    public float minZoom = 2f;
    public float maxZoom = 80f;

    private Camera cam;
    private Vector2 moveInput = Vector2.zero;
    private float zoomInput = 0f;

    void Start()
    {
        cam = Camera.main;
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnCameraMove += val => moveInput = val;
            InputManager.Instance.OnCameraZoom += val => zoomInput = val;
        }
    }

    void Update()
    {
        HandleMovement();
        HandleZoom();
    }

    void HandleMovement()
    {
        if (cam == null) return;
        Vector3 delta = new Vector3(moveInput.x, moveInput.y, 0f);
        cam.transform.position += delta * moveSpeed * Time.deltaTime;
    }

    void HandleZoom()
    {
        if (cam == null) return;
        cam.orthographicSize -= zoomInput * zoomSpeed * Time.deltaTime;
        cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minZoom, maxZoom);
    }
}
