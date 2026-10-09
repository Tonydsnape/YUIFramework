using SuperScrollView;
using UnityEngine.EventSystems;

namespace YUIFramework.Integrations
{
    public sealed class RoutedLoopGridView : LoopGridView, IUIListDragParticipant
    {
        public override void OnBeginDrag(PointerEventData data) { }
        public override void OnDrag(PointerEventData data) { }
        public override void OnEndDrag(PointerEventData data) { }
        public void BeginListDrag(PointerEventData data) => base.OnBeginDrag(data);
        public void DragList(PointerEventData data) => base.OnDrag(data);
        public void EndListDrag(PointerEventData data) => base.OnEndDrag(data);
    }
}
