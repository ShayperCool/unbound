using System.Collections.Generic;
using UnityEngine;

/// <summary>Logs the collision messages its GameObject receives, in order.</summary>
public class ModifyOrderLog : MonoBehaviour
{
    public static readonly List<string> events = new List<string>();

    void OnCollisionEnter(Collision c) => events.Add("enter");
    void OnCollisionStay(Collision c) => events.Add("stay");
    void OnCollisionExit(Collision c) => events.Add("exit");
}
