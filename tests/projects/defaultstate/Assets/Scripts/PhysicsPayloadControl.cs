using UnityEngine;
public sealed class PhysicsPayloadControl : MonoBehaviour
{
    public object[] collision;
    public object[] controller;
    void OnCollisionEnter(Collision value)
    {
        collision = new object[] { value.gameObject == value.collider.gameObject,
            value.transform == value.collider.transform, value.rigidbody == null, value.contactCount > 0 };
    }
    void OnControllerColliderHit(ControllerColliderHit value)
    {
        controller = new object[] { value.gameObject == value.collider.gameObject,
            value.transform == value.collider.transform, value.rigidbody == value.collider.attachedRigidbody, value.rigidbody == null };
    }
}
