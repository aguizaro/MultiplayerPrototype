using System.Collections.Generic;
using Unity.Netcode.Components;
using UnityEngine;

public class OwnerNetworkAnimator : NetworkAnimator
{
    protected override bool OnIsServerAuthoritative()
    {
        Debug.Log("NetwoerkAnimator: OwnerNetworkAnimator.OnIsServerAuthoritative() called. Returning false.");
        return false;
    }
}