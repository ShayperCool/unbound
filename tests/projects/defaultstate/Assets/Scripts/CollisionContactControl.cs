using UnityEngine;
using System;
using System.Collections.Generic;
public sealed class CollisionContactControl : MonoBehaviour
{
    public object[] enter, exit;
    void OnCollisionEnter2D(Collision2D collision)
    {
        var contacts = collision.contacts;
        bool before = collision.GetContact(0).normal != Vector2.zero;
        bool same = ReferenceEquals(contacts, collision.contacts);
        contacts[0] = default(ContactPoint2D);
        var copy = new ContactPoint2D[collision.contactCount]; collision.GetContacts(copy);
        var list = new List<ContactPoint2D>(); collision.GetContacts(list);
        enter = new object[] { collision.enabled, collision.contactCount, same, before,
            collision.GetContact(0).normal == Vector2.zero, copy[0].normal == Vector2.zero, list[0].normal == Vector2.zero };
    }
    void OnCollisionExit2D(Collision2D collision)
    {
        var contacts = collision.contacts;
        exit = new object[] { collision.enabled, collision.contactCount, contacts.Length,
            collision.GetContacts(new ContactPoint2D[0]), ReferenceEquals(contacts, collision.contacts) };
    }
}
