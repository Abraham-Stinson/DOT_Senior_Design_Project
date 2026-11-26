
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(ZeppelinMotor))]
public class ZeppelinLocalInput : MonoBehaviour
{
    public enum ControlScheme
    {
        WASD,
        ArrowKeys
    }

    [SerializeField] private ControlScheme controls =
        ControlScheme.WASD;

    private ZeppelinMotor motor;

    private void Awake()
    {
        motor = GetComponent<ZeppelinMotor>();
    }

    private void Update()
    {
        Vector2 input = Vector2.zero;

#if ENABLE_INPUT_SYSTEM

        Keyboard keyboard = Keyboard.current;

        if (keyboard != null)
        {
            if (controls == ControlScheme.WASD)
            {
                if (keyboard.wKey.isPressed) input.y += 1;
                if (keyboard.sKey.isPressed) input.y -= 1;
                if (keyboard.dKey.isPressed) input.x += 1;
                if (keyboard.aKey.isPressed) input.x -= 1;
            }
            else
            {
                if (keyboard.upArrowKey.isPressed) input.y += 1;
                if (keyboard.downArrowKey.isPressed) input.y -= 1;
                if (keyboard.rightArrowKey.isPressed) input.x += 1;
                if (keyboard.leftArrowKey.isPressed) input.x -= 1;
            }
        }

#elif ENABLE_LEGACY_INPUT_MANAGER

        if (controls == ControlScheme.WASD)
        {
            if (Input.GetKey(KeyCode.W)) input.y += 1;
            if (Input.GetKey(KeyCode.S)) input.y -= 1;
            if (Input.GetKey(KeyCode.D)) input.x += 1;
            if (Input.GetKey(KeyCode.A)) input.x -= 1;
        }
        else
        {
            if (Input.GetKey(KeyCode.UpArrow)) input.y += 1;
            if (Input.GetKey(KeyCode.DownArrow)) input.y -= 1;
            if (Input.GetKey(KeyCode.RightArrow)) input.x += 1;
            if (Input.GetKey(KeyCode.LeftArrow)) input.x -= 1;
        }

#endif

        motor.SetMoveInput(input);
    }

    private void OnDisable()
    {
        if (motor != null)
            motor.SetMoveInput(Vector2.zero);
    }
}
