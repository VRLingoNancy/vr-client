using UnityEngine;
using UnityEngine.InputSystem;

public class VRMenuToggle : MonoBehaviour
{
    public GameObject menu;              // Assign your menu canvas in Inspector
    public Transform playerCamera;       // Assign your XR Rig camera here
    public float distance = 2f;          // Distance in front of player
    [Header("Input Action")]
    public InputActionProperty toggleButton;

    void Update() {

        if (toggleButton.action.WasPressedThisFrame()) {
            menu.SetActive(!menu.activeSelf);

            if (menu.activeSelf) {
                Vector3 forward = playerCamera.forward;
                forward.Normalize();

                menu.transform.position = playerCamera.position + forward * distance;
                menu.transform.rotation = Quaternion.LookRotation(forward);
            }
        }
    }
}
