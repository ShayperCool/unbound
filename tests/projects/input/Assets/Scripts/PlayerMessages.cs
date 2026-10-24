using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Records what a PlayerInput tells its game object: messages (Send Messages), events
/// (Unity Events, as persistent listeners) and C# events.</summary>
public class PlayerMessages : MonoBehaviour
{
    void Message(string action, InputValue value)
    {
        string pressed;
        try { pressed = value.isPressed ? "true" : "false"; }
        catch (System.InvalidOperationException) { pressed = "null"; }
        ProbeLog.Add("player", "via", ProbeLog.Q("message"), "on", ProbeLog.Q(name), "action", ProbeLog.Q(action),
            "pressed", pressed, "value", ProbeLog.Q(InputProbe.Describe(value.Get())));
    }

    void OnMove(InputValue value) => Message("Move", value);
    void OnLook(InputValue value) => Message("Look", value);
    void OnAttack(InputValue value) => Message("Attack", value);
    void OnInteract(InputValue value) => Message("Interact", value);
    void OnCrouch(InputValue value) => Message("Crouch", value);
    void OnJump(InputValue value) => Message("Jump", value);
    void OnPrevious(InputValue value) => Message("Previous", value);
    void OnNext(InputValue value) => Message("Next", value);
    void OnSprint(InputValue value) => Message("Sprint", value);

    void OnControlsChanged(PlayerInput input) =>
        ProbeLog.Add("player", "via", ProbeLog.Q("message"), "on", ProbeLog.Q(name), "action", ProbeLog.Q("ControlsChanged"),
            "scheme", ProbeLog.Q(input.currentControlScheme));

    public void OnActionEvent(InputAction.CallbackContext context) => Context("event", context);

    public void OnActionTriggered(InputAction.CallbackContext context) => Context("csharp", context);

    void Context(string via, InputAction.CallbackContext context)
    {
        ProbeLog.Add("player", "via", ProbeLog.Q(via), "on", ProbeLog.Q(name), "action", ProbeLog.Q(context.action.name),
            "phase", ProbeLog.Q(context.phase.ToString()), "value", ProbeLog.Q(InputProbe.Describe(context.ReadValueAsObject())),
            "time", ProbeLog.F(context.time));
    }
}
