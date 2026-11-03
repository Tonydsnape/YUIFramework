using UnityEngine;

namespace YUIFramework.Tests
{
    [ExecuteAlways]
    public sealed class Stage14RequiredReferenceProbe : MonoBehaviour
    {
        public static int AwakeCalls;
        [UIRequiredReference] public UnityEngine.Object Required;
        public UnityEngine.Object Optional;
        private void Awake() { AwakeCalls++; }
    }
}
