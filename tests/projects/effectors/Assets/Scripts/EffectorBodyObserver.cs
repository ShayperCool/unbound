using UnityEngine;
public class EffectorBodyObserver : MonoBehaviour
{
    void OnCollisionEnter2D(Collision2D c)=>EffectorProbe.Event("enter");
    void OnCollisionStay2D(Collision2D c)=>EffectorProbe.Event("stay");
    void OnCollisionExit2D(Collision2D c)=>EffectorProbe.Event("exit");
    void OnTriggerEnter2D(Collider2D c)=>EffectorProbe.Event("triggerEnter");
    void OnTriggerExit2D(Collider2D c)=>EffectorProbe.Event("triggerExit");
}
