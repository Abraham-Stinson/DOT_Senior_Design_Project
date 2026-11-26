
using UnityEngine;
using PirateGame.Networking;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class NetworkTestPanelToggle : MonoBehaviour
{
    [SerializeField] private NetworkTestPanel testPanel;

    private void Awake()
    {
        if (testPanel == null)
            testPanel = GetComponent<NetworkTestPanel>();
    }

    private void Update()
    {
        bool togglePressed = false;

#if ENABLE_INPUT_SYSTEM

        togglePressed = Keyboard.current != null &&
            Keyboard.current.f1Key.wasPressedThisFrame;

#elif ENABLE_LEGACY_INPUT_MANAGER

        togglePressed = Input.GetKeyDown(KeyCode.F1);

#endif

        if (togglePressed && testPanel != null)
            testPanel.enabled = !testPanel.enabled;
    }
}
