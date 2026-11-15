using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SceneToolbar.Editor
{
    internal sealed class SceneToolbarDragData
    {
        internal const string DataKey = "ScenePin.SceneDragData";

        internal SceneToolbarDragData(string sceneGuid, SceneAsset sceneAsset, bool isPinned)
        {
            SceneGuid = sceneGuid;
            SceneAsset = sceneAsset;
            IsPinned = isPinned;
        }

        internal string SceneGuid { get; }
        internal SceneAsset SceneAsset { get; }
        internal bool IsPinned { get; }

        internal static SceneToolbarDragData Current =>
            DragAndDrop.GetGenericData(DataKey) as SceneToolbarDragData;
    }

    internal sealed class SceneToolbarDragManipulator : PointerManipulator
    {
        private const float DragThreshold = 5f;

        private readonly SceneToolbarDragData dragData;
        private Vector2 pointerDownPosition;
        private int pointerId = -1;
        private bool pointerDown;

        internal SceneToolbarDragManipulator(SceneToolbarDragData dragData)
        {
            this.dragData = dragData;
            activators.Add(new ManipulatorActivationFilter
            {
                button = MouseButton.LeftMouse
            });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        private void OnPointerDown(PointerDownEvent pointerEvent)
        {
            if (!CanStartManipulation(pointerEvent) || dragData == null)
            {
                return;
            }

            pointerDown = true;
            pointerId = pointerEvent.pointerId;
            pointerDownPosition = new Vector2(
                pointerEvent.position.x,
                pointerEvent.position.y);
            target.CapturePointer(pointerId);
        }

        private void OnPointerMove(PointerMoveEvent pointerEvent)
        {
            if (!pointerDown || pointerEvent.pointerId != pointerId)
            {
                return;
            }

            var currentPosition = new Vector2(
                pointerEvent.position.x,
                pointerEvent.position.y);
            if ((currentPosition - pointerDownPosition).sqrMagnitude <
                DragThreshold * DragThreshold)
            {
                return;
            }

            ReleasePointer();
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = dragData.SceneAsset != null
                ? new Object[] { dragData.SceneAsset }
                : System.Array.Empty<Object>();
            DragAndDrop.SetGenericData(SceneToolbarDragData.DataKey, dragData);
            DragAndDrop.StartDrag(
                dragData.IsPinned
                    ? "Reorder Scene Pin"
                    : "Add Scene Pin");
            pointerEvent.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent pointerEvent)
        {
            if (pointerEvent.pointerId == pointerId)
            {
                ReleasePointer();
            }
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent pointerEvent)
        {
            pointerDown = false;
            pointerId = -1;
        }

        private void ReleasePointer()
        {
            if (pointerId >= 0 && target.HasPointerCapture(pointerId))
            {
                target.ReleasePointer(pointerId);
            }

            pointerDown = false;
            pointerId = -1;
        }
    }
}
