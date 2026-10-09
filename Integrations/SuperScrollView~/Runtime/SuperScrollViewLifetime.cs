using System;
using UnityEngine;

namespace YUIFramework.Integrations
{
    public sealed class SuperScrollViewLifetime : MonoBehaviour
    {
        internal Action Disabled;
        internal Action Destroyed;
        private void OnDisable() => Disabled?.Invoke();
        private void OnDestroy()
        {
            var callback = Destroyed;
            Disabled = null;
            Destroyed = null;
            callback?.Invoke();
        }
    }
}
