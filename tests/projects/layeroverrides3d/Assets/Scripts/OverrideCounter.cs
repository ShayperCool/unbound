using UnityEngine;

/// <summary>Counts the collision and trigger messages its GameObject receives.</summary>
public class OverrideCounter : MonoBehaviour
{
    public int enter, stay, exit, triggerEnter, triggerStay, triggerExit;

    public void ResetStay() { stay = 0; triggerStay = 0; }

    void OnCollisionEnter(Collision c) => enter++;
    void OnCollisionStay(Collision c) => stay++;
    void OnCollisionExit(Collision c) => exit++;
    void OnTriggerEnter(Collider c) => triggerEnter++;
    void OnTriggerStay(Collider c) => triggerStay++;
    void OnTriggerExit(Collider c) => triggerExit++;
}
