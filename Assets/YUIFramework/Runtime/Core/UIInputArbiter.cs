using System;

namespace YUIFramework
{
    public sealed class UIInputArbiter
    {
        private readonly UIRootRuntime _runtime;

        internal UIInputArbiter(UIRootRuntime runtime)
        {
            _runtime = runtime;
        }

        public bool IsSceneInputAllowed =>
            !_runtime.IsDisposed &&
            !_runtime.InputLocks.IsLocked &&
            _runtime.Modals.Top == null &&
            !_runtime.Interaction.HasVisiblePage;

        public bool CanProcessScenePointer(int pointerId = -1) =>
            IsSceneInputAllowed &&
            !_runtime.EventSystem.IsPointerOverGameObject(pointerId);

        public bool CanProcessSceneKeyboard() =>
            IsSceneInputAllowed &&
            (_runtime.EventSystem.currentSelectedGameObject == null ||
             !_runtime.EventSystem.currentSelectedGameObject.transform.IsChildOf(_runtime.Root.transform));

        public IDisposable Acquire(object owner, string reason, params UILayer[] allowedLayers) =>
            _runtime.InputLocks.Acquire(owner, reason, allowedLayers);
    }
}
