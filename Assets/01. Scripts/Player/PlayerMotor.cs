using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMotor : MonoBehaviour
{
    public static PlayerMotor Instance { get; private set; }

    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 720f;

    private Rigidbody rb;
    private Vector2 keyboardInput;
    private Vector2 joystickInput;

    private void Awake()
    {
        Instance = this;
        rb = GetComponent<Rigidbody>();
    }

    public void SetKeyboardInput(Vector2 input)
    {
        keyboardInput = input;
    }

    public void SetJoystickInput(Vector2 input)
    {
        joystickInput = input;
    }

    private void FixedUpdate()
    {
        Vector2 combined = keyboardInput + joystickInput;
        if (combined.sqrMagnitude > 1f)
        {
            combined = combined.normalized;
        }

        Vector3 moveDirection = new Vector3(combined.x, 0f, combined.y);

        Vector3 velocity = moveDirection * moveSpeed;
        velocity.y = rb.linearVelocity.y;
        rb.linearVelocity = velocity;

        if (moveDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
        }
    }
}
