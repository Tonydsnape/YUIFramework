using SuperScrollView;
using UnityEngine.EventSystems;

namespace YUIFramework.Integrations
{
    // EventSystem also enumerates native handlers on this GameObject. Only the
    // ScrollRect arbiter may enter the native drag path.
    public sealed class RoutedLoopListView : LoopListView2, IUIListDragParticipant
    {
        public override void OnBeginDrag(PointerEventData data) { }
        public override void OnDrag(PointerEventData data) { }
        public override void OnEndDrag(PointerEventData data) { }
        public void BeginListDrag(PointerEventData data) => base.OnBeginDrag(data);
        public void DragList(PointerEventData data) => base.OnDrag(data);
        public void EndListDrag(PointerEventData data) => base.OnEndDrag(data);
    }
}
