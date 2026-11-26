
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class ZeppelinMotor : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Movement Bounds")]
    [SerializeField] private float minX = -9.5f;
    [SerializeField] private float maxX = 9.5f;
    [SerializeField] private float minY = -3.2f;
    [SerializeField] private float maxY = 5.1f;

    private Rigidbody2D rb;
    private Vector2 moveInput;
    private bool movementEnabled = true;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public void SetMoveInput(Vector2 input)
    {
        moveInput = Vector2.ClampMagnitude(input, 1f);
    }

    public void SetMovementEnabled(bool value)
    {
        movementEnabled = value;

        if (!value)
            moveInput = Vector2.zero;
    }

    private void FixedUpdate()
    {
        if (!movementEnabled)
            return;

        Vector2 targetPosition = rb.position +
            moveInput * moveSpeed * Time.fixedDeltaTime;

        targetPosition.x = Mathf.Clamp(
            targetPosition.x, minX, maxX);

        targetPosition.y = Mathf.Clamp(
            targetPosition.y, minY, maxY);

        rb.MovePosition(targetPosition);
    }
}
