using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YUIFramework
{
    public interface IUIListDragParticipant
    {
        void BeginListDrag(PointerEventData data);
        void DragList(PointerEventData data);
        void EndListDrag(PointerEventData data);
    }

    public class UINestedScrollRect : ScrollRect
    {
        private ScrollRect _parentScroll;
        private ScrollRect _receiver;
        private PointerEventData _pointer;
        private bool _ownDrag;
        private IUIListDragParticipant _participant;
        private readonly List<CanvasGroup> _groups = new List<CanvasGroup>();
        private bool _interactionAllowed = true;

        public void SetParentScroll(ScrollRect parent) => _parentScroll = parent;
        public void SetDragParticipant(IUIListDragParticipant participant) => _participant = participant;
        public int? ActivePointerId => _pointer?.pointerId;
        protected override void Awake()
        {
            base.Awake();
            if (_parentScroll == null && transform.parent != null)
                _parentScroll = transform.parent.GetComponentInParent<ScrollRect>();
        }
        public override void OnBeginDrag(PointerEventData eventData)
        {
            if (!IsActive() || !_interactionAllowed || eventData.button != PointerEventData.InputButton.Left) return;
            if (_pointer != null)
            {
                // Native layout rebasing sends a copied event without touch identity.
                // Rebase the current owner, never reinterpret it as a new gesture.
                if (_ownDrag) base.OnBeginDrag(_pointer);
                return;
            }
            var delta = eventData.position - eventData.pressPosition;
            if (delta.sqrMagnitude == 0) delta = eventData.delta;
            var horizontalDrag = Mathf.Abs(delta.x) > Mathf.Abs(delta.y);
            var accepts = horizontalDrag ? horizontal : vertical;
            _pointer = eventData;
            if (!accepts && _parentScroll != null && _parentScroll.isActiveAndEnabled)
            {
                _receiver = _parentScroll;
                _receiver.OnInitializePotentialDrag(eventData);
                _receiver.OnBeginDrag(eventData);
            }
            else if (accepts)
            {
                _ownDrag = true;
                base.OnBeginDrag(eventData);
                _participant?.BeginListDrag(eventData);
            }
        }
        public override void OnDrag(PointerEventData eventData)
        {
            if (_pointer == null || eventData.pointerId != _pointer.pointerId) return;
            if (_receiver != null) _receiver.OnDrag(eventData);
            else if (_ownDrag)
            {
                base.OnDrag(eventData);
                _participant?.DragList(eventData);
            }
        }
        public override void OnEndDrag(PointerEventData eventData)
        {
            if (_pointer == null || eventData.pointerId != _pointer.pointerId) return;
            CancelDrag();
        }
        public void CancelDrag()
        {
            var receiver = _receiver;
            var pointer = _pointer;
            var ownDrag = _ownDrag;
            _receiver = null;
            _pointer = null;
            _ownDrag = false;
            if (pointer == null) return;
            if (receiver != null) receiver.OnEndDrag(pointer);
            else if (ownDrag)
            {
                try { base.OnEndDrag(pointer); }
                finally { _participant?.EndListDrag(pointer); }
            }
        }
        protected override void OnDisable()
        {
            try { CancelDrag(); }
            finally { base.OnDisable(); }
        }
        protected override void OnEnable()
        {
            base.OnEnable();
            OnCanvasGroupChanged();
        }
        protected override void OnCanvasGroupChanged()
        {
            base.OnCanvasGroupChanged();
            _interactionAllowed = true;
            for (var current = transform; current != null; current = current.parent)
            {
                current.GetComponents(_groups);
                var stop = false;
                foreach (var group in _groups)
                {
                    _interactionAllowed &= group.interactable && group.blocksRaycasts;
                    stop |= group.ignoreParentGroups;
                }
                if (stop || !_interactionAllowed) break;
            }
            _groups.Clear();
            if (!_interactionAllowed) CancelDrag();
        }
    }
}
