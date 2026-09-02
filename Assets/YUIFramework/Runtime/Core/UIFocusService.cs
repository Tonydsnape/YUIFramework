using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YUIFramework
{
    public sealed class UIFocusService
    {
        private readonly EventSystem _eventSystem;
        private readonly List<BaseContext> _active = new List<BaseContext>();
        private readonly Dictionary<BaseContext, GameObject> _previous =
            new Dictionary<BaseContext, GameObject>();

        internal UIFocusService(EventSystem eventSystem)
        {
            _eventSystem = eventSystem ?? throw new ArgumentNullException(nameof(eventSystem));
        }

        public void Activate(BaseContext context)
        {
            if (context == null)
            {
                return;
            }

            _previous[context] = _eventSystem.currentSelectedGameObject;
            _active.Remove(context);
            _active.Add(context);
            Focus(context);
        }

        public void Deactivate(BaseContext context)
        {
            if (context == null)
            {
                return;
            }

            var wasTop = _active.Count > 0 &&
                         ReferenceEquals(_active[_active.Count - 1], context);
            _active.Remove(context);
            _previous.TryGetValue(context, out var previous);
            _previous.Remove(context);
            if (!wasTop)
            {
                Refresh(_ => true);
                return;
            }

            if (IsFocusable(previous))
            {
                _eventSystem.SetSelectedGameObject(previous);
                return;
            }

            FocusTop();
        }

        public void Refresh(Func<BaseContext, bool> isEligible)
        {
            if (isEligible == null)
            {
                throw new ArgumentNullException(nameof(isEligible));
            }

            BaseContext top = null;
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var context = _active[i];
                if (context?.ViewObject != null &&
                    context.ViewObject.activeInHierarchy &&
                    isEligible(context))
                {
                    top = context;
                    break;
                }
            }

            if (top == null)
            {
                _eventSystem.SetSelectedGameObject(null);
                return;
            }

            var selected = _eventSystem.currentSelectedGameObject;
            if (selected != null &&
                selected.transform.IsChildOf(top.ViewObject.transform) &&
                IsFocusable(selected))
            {
                return;
            }

            Focus(top);
        }

        public void Focus(BaseContext context)
        {
            var target = ResolveDefault(context);
            _eventSystem.SetSelectedGameObject(target);
        }

        public void FocusTop()
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var context = _active[i];
                if (context?.ViewObject != null && context.ViewObject.activeInHierarchy)
                {
                    Focus(context);
                    return;
                }
            }

            _eventSystem.SetSelectedGameObject(null);
        }

        public void Clear()
        {
            _active.Clear();
            _previous.Clear();
            if (_eventSystem != null)
            {
                _eventSystem.SetSelectedGameObject(null);
            }
        }

        private static GameObject ResolveDefault(BaseContext context)
        {
            if (context == null)
            {
                return null;
            }

            var declared = context.DefaultFocus;
            if (IsFocusable(declared))
            {
                return declared;
            }

            var viewObject = context.ViewObject;
            if (viewObject == null || !viewObject.activeInHierarchy)
            {
                return null;
            }

            var selectable = viewObject.GetComponentInChildren<Selectable>(false);
            return selectable != null && selectable.IsInteractable()
                ? selectable.gameObject
                : null;
        }

        private static bool IsFocusable(GameObject target)
        {
            if (target == null || !target.activeInHierarchy)
            {
                return false;
            }

            var selectable = target.GetComponent<Selectable>();
            return selectable == null || selectable.IsInteractable();
        }
    }
}
