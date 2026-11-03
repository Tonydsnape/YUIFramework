using System;
using UnityEngine;

namespace YUIFramework
{
    public sealed class SystemUISampleDriver : MonoBehaviour
    {
        private Action _tick;
        public void Bind(Action tick) => _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        private void Update() => _tick?.Invoke();
        private void OnDestroy() => _tick = null;
    }
}
