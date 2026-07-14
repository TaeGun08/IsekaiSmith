using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerMotor))]
public class PlayerKeyboardInput : MonoBehaviour
{
    private PlayerMotor motor;

    private void Awake()
    {
        motor = GetComponent<PlayerMotor>();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        Vector2 raw = Vector2.zero;

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) raw.y += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) raw.y -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) raw.x += 1f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) raw.x -= 1f;

        motor.SetKeyboardInput(Vector2.ClampMagnitude(raw, 1f));
    }
}
