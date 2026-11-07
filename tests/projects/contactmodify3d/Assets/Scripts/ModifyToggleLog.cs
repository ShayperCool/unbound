using UnityEngine;

public class ModifyToggleLog : MonoBehaviour
{
    void OnCollisionEnter(Collision collision)
    {
        var collider = GetComponent<Collider>();
        collider.hasModifiableContacts = !collider.hasModifiableContacts;
    }
}
