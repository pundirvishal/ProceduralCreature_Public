using UnityEngine;

public class MouseFollower2D : MonoBehaviour
{
    private Camera mainCamera;

    void Awake()
    {
        // Find and store a reference to the main camera.
        // The camera must have the "MainCamera" tag for this to work.
        mainCamera = Camera.main;
    }

    void Update()
    {
        // We only want to do something if the left mouse button is held down.
        if (Input.GetMouseButton(0))
        {
            // Get the mouse position in screen coordinates (pixels).
            Vector3 mouseScreenPosition = Input.mousePosition;

            // To convert this to a world point, we need to give it a Z-depth.
            // We use the object's current Z position in relation to the camera.
            // This ensures the object stays on its original Z-plane.
            mouseScreenPosition.z = mainCamera.WorldToScreenPoint(transform.position).z;

            // Convert the final screen position to a world position.
            Vector3 mouseWorldPosition = mainCamera.ScreenToWorldPoint(mouseScreenPosition);

            // Update this object's position to the calculated world position.
            transform.position = mouseWorldPosition;
        }
    }
}