using System;
using UnityEngine;

namespace YUIFramework.Tests
{
    [DefaultExecutionOrder(32000)]
    public sealed class Stage15DestroyAfterStartProbe : MonoBehaviour
    {
        public Action Started;
        public Action Destroyed;
        public bool DestroyHost;
        private void Start()
        {
            Started?.Invoke();
            if (!DestroyHost) return;
            DestroyImmediate(gameObject);
            Destroyed?.Invoke();
        }
    }
}
