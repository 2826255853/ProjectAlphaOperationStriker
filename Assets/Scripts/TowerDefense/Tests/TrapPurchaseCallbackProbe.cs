#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using UnityEngine;

// Exercises real synchronous callbacks during grid creation, without a production test hook.
[ExecuteAlways]
public sealed class TrapPurchaseCallbackProbe : MonoBehaviour
{
    public Action Callback;

    private void OnTransformChildrenChanged()
    {
        Action callback = Callback;
        Callback = null;
        callback?.Invoke();
    }
}
#endif
