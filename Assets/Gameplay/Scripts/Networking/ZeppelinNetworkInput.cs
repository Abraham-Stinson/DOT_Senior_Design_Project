
using UnityEngine;
using FishNet.Object;
using FishNet.Transporting;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(ZeppelinMotor))]
[RequireComponent(typeof(Rigidbody2D))]
public class ZeppelinNetworkInput : NetworkBehaviour
{
    [Header("Network Input")]
    [SerializeField] private float sendRate = 30f;
    [SerializeField] private float inputTimeout = 0.35f;

    private ZeppelinMotor motor;
    private Rigidbody2D rb;

    private float nextSendTime;
    private float lastReceivedTime;

    private uint sequence;
    private uint lastReceivedSequence;

    private void Awake()
    {
        motor = GetComponent<ZeppelinMotor>();
        rb = GetComponent<Rigidbody2D>();

        motor.enabled = false;
        rb.simulated = false;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        rb.simulated = true;
        motor.enabled = true;
        motor.SetMoveInput(Vector2.zero);

        lastReceivedSequence = 0;
        lastReceivedTime = Time.unscaledTime;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        sequence = 0;
        nextSendTime = 0f;

        if (!IsServerInitialized)
        {
            motor.enabled = false;
            rb.simulated = false;
        }
    }

    private void Update()
    {
        if (IsServerInitialized)
        {
            if (Time.unscaledTime - lastReceivedTime >
                inputTimeout)
            {
                motor.SetMoveInput(Vector2.zero);
            }
        }

        if (!IsClientInitialized || !IsOwner)
            return;

        if (Time.unscaledTime < nextSendTime)
            return;

        nextSendTime = Time.unscaledTime +
            1f / Mathf.Max(1f, sendRate);

        Vector2 input = ReadInput();

        SubmitInputServerRpc(input, ++sequence);
    }

    public bool SuppressLocalInput { get; set; }

    private Vector2 ReadInput()
    {
        Vector2 input = Vector2.zero;

        if (SuppressLocalInput || !Application.isFocused)
            return input;

#if ENABLE_INPUT_SYSTEM

        Keyboard keyboard = Keyboard.current;

        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed) input.y += 1;
            if (keyboard.sKey.isPressed) input.y -= 1;
            if (keyboard.dKey.isPressed) input.x += 1;
            if (keyboard.aKey.isPressed) input.x -= 1;
        }

#elif ENABLE_LEGACY_INPUT_MANAGER

        if (Input.GetKey(KeyCode.W)) input.y += 1;
        if (Input.GetKey(KeyCode.S)) input.y -= 1;
        if (Input.GetKey(KeyCode.D)) input.x += 1;
        if (Input.GetKey(KeyCode.A)) input.x -= 1;

#endif

        return Vector2.ClampMagnitude(input, 1f);
    }

    [ServerRpc(RequireOwnership = true)]
    private void SubmitInputServerRpc(
        Vector2 input,
        uint inputSequence,
        Channel channel = Channel.Unreliable)
    {
        if (!IsServerInitialized)
            return;

        if (inputSequence <= lastReceivedSequence)
            return;

        if (float.IsNaN(input.x) ||
            float.IsNaN(input.y) ||
            float.IsInfinity(input.x) ||
            float.IsInfinity(input.y))
            return;

        lastReceivedSequence = inputSequence;
        lastReceivedTime = Time.unscaledTime;

        motor.SetMoveInput(
            Vector2.ClampMagnitude(input, 1f));
    }

    public override void OnStopServer()
    {
        motor.SetMoveInput(Vector2.zero);
        motor.enabled = false;
        rb.simulated = false;

        base.OnStopServer();
    }
}
