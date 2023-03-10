// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GraphViewBase {
    public abstract class GraphView : VisualElement {

        private const int k_FrameBorder = 30;
        private const int k_PanAreaWidth = 100;
        private const int k_PanSpeed = 4;
        private const int k_PanInterval = 10;
        private const float k_MinSpeedFactor = 0.5f;
        private const float k_MaxSpeedFactor = 2.5f;
        private const float k_MaxPanSpeed = k_MaxSpeedFactor * k_PanSpeed;
        private const float k_PanAreaWidthAndMinSpeedFactor = k_PanAreaWidth + k_MinSpeedFactor;

        private static StyleSheet s_DefaultStyle;

        private readonly Dictionary<int, Layer> m_ContainerLayers;
        private readonly VisualElement m_GridBackground;
        private readonly Dictionary<Actions, Action> internalActions = null;
        public ShortcutHandler shortcutHandler = null;
        public abstract void OnActionExecuted(Actions actionType, object data = null);

        #region Constructor
        protected GraphView() {

            //
            // GraphView - Level 0
            //
            // Style
            styleSheets.Add(DefaultStyle);
            AddToClassList("graph-view");
            usageHints = UsageHints.MaskContainer; // Should equate to RenderHints.ClipWithScissors

            // Manipulators
            m_Zoomer = new();
            this.AddManipulator(m_Zoomer);
            this.AddManipulator(new DragAndDropManipulator(this));

            //
            // Graph View Container & Grid - Level 1
            //
            // Root Container
            m_GridBackground = new GridBackground();
            hierarchy.Add(m_GridBackground);

            //
            // Content Container - Level 2
            //
            // Content Container
            ContentContainer = new(this);
            m_GridBackground.Add(ContentContainer);

            //
            // Other Initialization
            //
            // Layers
            m_ContainerLayers = new();

            // Create Marquee 
            m_Marquee = new();

            // Panning
            m_PanSchedule = schedule
                .Execute(Pan)
                .Every(k_PanInterval)
                .StartingIn(k_PanInterval);
            m_PanSchedule.Pause();

            // Focus
            focusable = true;
            Focus();

            // Shortcut Handler
            shortcutHandler = new ShortcutHandler();
            internalActions = new Dictionary<Actions, Action>() {
                { Actions.Frame, Frame },
            };
        }

        #endregion

        #region Helper Classes
        public class Layer : VisualElement {
            public Layer() => pickingMode = PickingMode.Ignore;
        }
        #endregion

        #region Properties
        private static StyleSheet DefaultStyle {
            get {
                if (s_DefaultStyle == null) {
                    s_DefaultStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath("09510940da67cc447a0e207f2731c573"));
                }
                return s_DefaultStyle;
            }
        }

        public ViewTransformChanged OnViewTransformChanged { get; set; }
        protected internal GraphElementContainer ContentContainer { get; }
        internal ITransform ViewTransform => ContentContainer.transform;
        #endregion

        #region Factories
        public virtual BaseEdge CreateEdge() => new Edge();

        public virtual BasePort CreatePort(Orientation orientation, Direction direction, PortCapacity capacity)
            => new(orientation, direction, capacity);
        #endregion

        #region View Transform
        public delegate void ViewTransformChanged(GraphElementContainer contentContainer);

        //public delegate void MouseUp(MouseUpEvent evt);
        //public MouseUp OnMouseUp { get; set; }

        //public delegate void MouseMove(MouseMoveEvent evt);
        //public MouseMove OnMouseMove { get; set; }

        public void UpdateViewTransform(Vector3 newPosition)
            => UpdateViewTransform(newPosition, ViewTransform.scale);

        public void UpdateViewTransform(Vector3 newPosition, Vector3 newScale) {
            float validateFloat = newPosition.x + newPosition.y + newPosition.z + newScale.x + newScale.y + newScale.z;
            if (float.IsInfinity(validateFloat) || float.IsNaN(validateFloat)) { return; }

            ViewTransform.scale = newScale;
            ViewTransform.position = newPosition;

            OnViewTransformChanged?.Invoke(ContentContainer);
            OnActionExecuted(Actions.ViewPortChanged);
        }

        public GraphElementContainer GetContentContainer() => ContentContainer;

        #endregion

        #region Pan
        private IPositionable m_PanElement;
        private Vector2 m_PanOriginDiff;
        private readonly IVisualElementScheduledItem m_PanSchedule;

        internal void TrackElementForPan(IPositionable element) {
            m_PanOriginDiff = Vector2.zero;
            m_PanElement = element;
            m_PanSchedule.Resume();
        }

        internal Vector2 UntrackElementForPan(IPositionable element, bool resetView = false) {
            if (element == m_PanElement) {
                m_PanSchedule.Pause();
                m_PanElement = null;
                if (resetView) { UpdateViewTransform((Vector2)ViewTransform.position + m_PanOriginDiff); }
                return m_PanOriginDiff;
            }
            return Vector2.zero;
        }

        private Vector2 GetEffectivePanSpeed(Vector2 mousePos) {
            Vector2 effectiveSpeed = Vector2.zero;

            if (mousePos.x <= k_PanAreaWidth) {
                effectiveSpeed.x = -((k_PanAreaWidth - mousePos.x) / k_PanAreaWidthAndMinSpeedFactor) * k_PanSpeed;
            } else if (mousePos.x >= m_GridBackground.layout.width - k_PanAreaWidth) {
                effectiveSpeed.x = (mousePos.x - (m_GridBackground.layout.width - k_PanAreaWidth))
                    / k_PanAreaWidthAndMinSpeedFactor * k_PanSpeed;
            }

            if (mousePos.y <= k_PanAreaWidth) {
                effectiveSpeed.y = -((k_PanAreaWidth - mousePos.y) / k_PanAreaWidthAndMinSpeedFactor) * k_PanSpeed;
            } else if (mousePos.y >= m_GridBackground.layout.height - k_PanAreaWidth) {
                effectiveSpeed.y = (mousePos.y - (m_GridBackground.layout.height - k_PanAreaWidth))
                    / k_PanAreaWidthAndMinSpeedFactor * k_PanSpeed;
            }

            return Vector2.ClampMagnitude(effectiveSpeed, k_MaxPanSpeed);
        }

        private void Pan() {

            // Use element center as the point to test against the bounds of the pan area
            Vector2 elementPositionWorld = m_PanElement.GetGlobalCenter();

            // If the point has entered the bounds of the pan area, calculate how fast we want to pan
            Vector2 speed = GetEffectivePanSpeed(elementPositionWorld);
            if (Vector2.zero == speed) { return; }

            // Record changes in pan
            m_PanOriginDiff += speed;

            // Update the view transform (to pan)
            UpdateViewTransform((Vector2)ViewTransform.position - speed);

            // Speed is scaled according to the current zoom level
            Vector2 localSpeed = speed / CurrentScale;

            // Set position
            m_PanElement.ApplyDeltaToPosition(localSpeed);
        }
        #endregion

        #region Layers
        internal void ChangeLayer(GraphElement element) { GetLayer(element.Layer).Add(element); }

        private VisualElement GetLayer(int index) {
            if (!m_ContainerLayers.TryGetValue(index, out Layer layer)) {
                layer = new() { name = $"Layer {index}" };
                m_ContainerLayers[index] = layer;

                // TODO
                int indexOfLayer = m_ContainerLayers.OrderBy(t => t.Key).Select(t => t.Value).ToList().IndexOf(layer);
                ContentContainer.Insert(indexOfLayer, layer);
            }
            return layer;
        }
        #endregion

        #region Zoom
        private readonly ZoomManipulator m_Zoomer;

        public float MinScale {
            get => m_Zoomer.MinScale;
            set {
                m_Zoomer.MinScale = Math.Min(value, ZoomManipulator.DefaultMinScale);
                ValidateTransform();
            }
        }

        public float MaxScale {
            get => m_Zoomer.MaxScale;
            set {
                m_Zoomer.MaxScale = Math.Max(value, ZoomManipulator.DefaultMaxScale);
                ValidateTransform();
            }
        }

        public float ScaleStep {
            get => m_Zoomer.ScaleStep;
            set {
                m_Zoomer.ScaleStep = Math.Min(value, (MaxScale - MinScale) / 2);
                ValidateTransform();
            }
        }

        public float ReferenceScale {
            get => m_Zoomer.ReferenceScale;
            set {
                m_Zoomer.ReferenceScale = Math.Clamp(value, MinScale, MaxScale);
                ValidateTransform();
            }
        }

        public float CurrentScale => ViewTransform.scale.x;

        protected void ValidateTransform() {
            if (ContentContainer == null) { return; }
            Vector3 transformScale = ViewTransform.scale;

            transformScale.x = Mathf.Clamp(transformScale.x, MinScale, MaxScale);
            transformScale.y = Mathf.Clamp(transformScale.y, MinScale, MaxScale);

            UpdateViewTransform(ViewTransform.position, transformScale);
        }
        #endregion

        #region Event Handlers
        private readonly Marquee m_Marquee;
        private bool m_DraggingView;
        private bool m_DraggingMarquee;
        public Vector2 mousePosition;

        [EventInterest(typeof(MouseMoveEvent), typeof(DragOfferEvent), typeof(DragEvent), typeof(DragEndEvent), typeof(DragCancelEvent),
            typeof(DropEnterEvent), typeof(DropEvent), typeof(DropExitEvent))]
        protected override void ExecuteDefaultActionAtTarget(EventBase evt) {
            base.ExecuteDefaultActionAtTarget(evt);
            
            switch (evt.eventTypeId) {
                case long v when v == MouseMoveEvent.TypeId():
                    mousePosition = ((MouseMoveEvent)evt).mousePosition;
                    break;
                case long v when v == DragOfferEvent.TypeId():
                    OnDragOffer((DragOfferEvent)evt);
                    break;
                case long v when v == DragEvent.TypeId():
                    OnDrag((DragEvent)evt);
                    break;
                case long v when v == DragEndEvent.TypeId():
                    OnDragEnd((DragEndEvent)evt);
                    break;
                case long v when v == DragCancelEvent.TypeId():
                    OnDragCancel((DragCancelEvent)evt);
                    break;
                case long v when v == DropEnterEvent.TypeId():
                    OnDropEnter((DropEnterEvent)evt);
                    break;
                case long v when v == DropEvent.TypeId():
                    OnDrop((DropEvent)evt);
                    break;
                case long v when v == DropExitEvent.TypeId():
                    OnDropExit((DropExitEvent)evt);
                    break;
            }
        }

        public void OnDragOffer(DragOfferEvent e, bool forceViewDrag = false) {
            if (forceViewDrag || IsViewDrag(e)) {
                // Accept Drag
                e.AcceptDrag(this);
                e.StopImmediatePropagation();
                m_DraggingView = true;

                // Assume focus
                Focus();
            } else if (IsMarqueeDrag(e)) {
                // Accept Drag
                e.AcceptDrag(this);
                e.StopImmediatePropagation();
                m_DraggingMarquee = true;

                // Clear selection if this is an exclusive select 
                bool additive = e.modifiers.IsShift();
                bool subtractive = e.modifiers.IsActionKey();
                bool exclusive = !(additive ^ subtractive);
                if (exclusive) { ContentContainer.ClearSelection(true); }

                // Create marquee
                Add(m_Marquee);
                Vector2 position = this.WorldToLocal(e.mousePosition);
                m_Marquee.Coordinates = new() {
                    start = position,
                    end = position
                };

                // Assume focus
                Focus();
            }
        }

        private void OnDrag(DragEvent e) {
            if (m_DraggingMarquee) {
                e.StopImmediatePropagation();

                // TODO - MouseMoveEvent doesn't correctly report mouse button so I don't check IsMarqueeDrag 
                m_Marquee.End = this.WorldToLocal(e.mousePosition);
            } else if (m_DraggingView) {
                e.StopImmediatePropagation();
                // TODO - MouseMoveEvent doesn't correctly report mouse button so I don't check IsViewDrag 
                UpdateViewTransform(ViewTransform.position + (Vector3)e.mouseDelta);
            }
        }

        private void OnDragEnd(DragEndEvent e) {
            if (m_DraggingMarquee) {
                // Remove marquee
                e.StopImmediatePropagation();
                m_Marquee.RemoveFromHierarchy();

                // Ignore if the rectangle is infinitely small
                Rect selectionRect = m_Marquee.SelectionRect;
                if (selectionRect.size == Vector2.zero) {
                    m_DraggingMarquee = false;
                    return;
                }

                // Select elements that overlap the marquee
                bool additive = e.modifiers.IsShift();
                bool subtractive = e.modifiers.IsActionKey();
                bool exclusive = !(additive ^ subtractive);
                bool somethingSelected = false;
                foreach (GraphElement element in ContentContainer.ElementsAll) {
                    Rect localSelRect = this.ChangeCoordinatesTo(element, selectionRect);
                    if (element.Overlaps(localSelRect)) {
                        element.Selected = exclusive || additive;
                    } else if (exclusive) {
                        element.Selected = false;
                    }
                    if (element.Selected) {
                        somethingSelected = true;
                    }
                }
                if (somethingSelected) {
                    OnActionExecuted(Actions.SelectionChanged);
                }
                m_DraggingMarquee = false;
            } else if (m_DraggingView) {
                e.StopImmediatePropagation();
                m_DraggingView = false;
            }
        }

        private void OnDragCancel(DragCancelEvent e) {
            if (m_DraggingMarquee) {
                e.StopImmediatePropagation();
                m_Marquee.RemoveFromHierarchy();
                m_DraggingMarquee = false;
            } else if (m_DraggingView) {
                e.StopImmediatePropagation();
                UpdateViewTransform(ViewTransform.position + Vector3.Scale(e.mouseDelta, ViewTransform.scale));
                m_DraggingView = false;
            }
        }

        private void OnDropEnter(DropEnterEvent e) {
            if (e.GetUserData() is IDropPayload dropPayload &&
                typeof(BaseEdge).IsAssignableFrom(dropPayload.GetPayloadType())) {
                // Consume event
                e.StopImmediatePropagation();
            }
        }

        public void OnDrop(DropEvent e) {

            if (e.GetUserData() is IDropPayload dropPayload &&
                typeof(BaseEdge).IsAssignableFrom(dropPayload.GetPayloadType())) {
                // Consume event
                e.StopImmediatePropagation();

                // Delete edges 
                for (int i = dropPayload.GetPayload().Count - 1; i >= 0; i--) {
                    // Grab the edge
                    BaseEdge edge = (BaseEdge)dropPayload.GetPayload()[i];

                    // Delete real edge
                    if (edge.IsRealEdge()) {
                        OnActionExecuted(Actions.EdgeDelete, edge);
                    }else {
                        OnActionExecuted(Actions.EdgeDrop, edge);
                        RemoveElement(edge);
                    }
                }
            }
        }

        private void OnDropExit(DropExitEvent e) {
            if (e.GetUserData() is IDropPayload dropPayload &&
                typeof(BaseEdge).IsAssignableFrom(dropPayload.GetPayloadType())) {
                // Consume event
                e.StopImmediatePropagation();
            }
        }

        private bool IsMarqueeDrag<T>(DragAndDropEvent<T> e) where T : DragAndDropEvent<T>, new() {
            if ((MouseButton)e.button != MouseButton.LeftMouse) { return false; }
            if (e.modifiers.IsNone()) { return true; }
            if (e.modifiers.IsExclusiveShift()) { return true; }
            if (e.modifiers.IsExclusiveActionKey()) { return true; }
            return false;
        }

        public bool IsViewDrag<T>(DragAndDropEvent<T> e) where T : DragAndDropEvent<T>, new() {
            if (e.modifiers.HasFlag(EventModifiers.Alt)) { /*Debug.Log("view drag");*/ return true; }
            if ((MouseButton)e.button != MouseButton.MiddleMouse) { return false; }
            if (!e.modifiers.IsNone()) { return false; }
            return true;
        }
        #endregion

        #region Keybinding
        protected override void ExecuteDefaultAction(EventBase baseEvent) {
            if (baseEvent is not KeyDownEvent evt) { return; }
            ExecuteShortcutHandler(evt.keyCode, evt.modifiers);
        }

        protected void ExecuteShortcutHandler(KeyCode keyCode, EventModifiers modifiers) {
            if (panel.GetCapturingElement(PointerId.mousePointerId) != null) {
                return;
            }

            Actions keyAction = shortcutHandler.Execute(keyCode, modifiers);
            if (internalActions.ContainsKey(keyAction)) {
                internalActions[keyAction]();
            }
            OnActionExecuted(keyAction);
        }

        #endregion

        #region Add / Remove Elements from Hierarchy
        public void AddElement(GraphElement graphElement) {
            graphElement.Graph = this;
            GetLayer(graphElement.Layer).Add(graphElement);
        }

        public void RemoveElement(GraphElement graphElement) {
            UntrackElementForPan(graphElement); // Stop panning if we were panning
            graphElement.RemoveFromHierarchy();
            graphElement.Graph = null;
        }
        #endregion

        #region Ports
        public void ConnectPorts(BasePort input, BasePort output) {
            AddElement(input.ConnectTo(output));
        }

        internal void IlluminateCompatiblePorts(BasePort port) {
            foreach (BasePort otherPort in ContentContainer.Ports) {
                otherPort.Highlight = port.CanConnectTo(otherPort);
            }
        }

        internal void IlluminateAllPorts() {
            foreach (BasePort otherPort in ContentContainer.Ports) { otherPort.Highlight = true; }
        }
        #endregion

        #region Framing
        protected void Frame() {
            // Construct rect for selected and unselected elements
            Rect rectToFitSelected = ContentContainer.layout;
            Rect rectToFitUnselected = rectToFitSelected;
            bool reachedFirstSelected = false;
            bool reachedFirstUnselected = false;
            foreach (GraphElement ge in ContentContainer.ElementsAll) {
                // TODO: edge control is the VisualElement with actual dimensions
                // VisualElement ve = ge is BaseEdge edge ? edge.EdgeControl : ge;
                if (ge.Selected) {
                    if (!reachedFirstSelected) {
                        rectToFitSelected = ge.ChangeCoordinatesTo(ContentContainer, ge.Rect());
                        reachedFirstSelected = true;
                    } else {
                        rectToFitSelected = RectUtils.Encompass(rectToFitSelected,
                            ge.ChangeCoordinatesTo(ContentContainer, ge.Rect()));
                    }
                } else if (!reachedFirstSelected) // Don't bother if we already have at least one selected item
                  {
                    if (!reachedFirstUnselected) {
                        rectToFitUnselected = ge.ChangeCoordinatesTo(ContentContainer, ge.Rect());
                        reachedFirstUnselected = true;
                    } else {
                        rectToFitUnselected = RectUtils.Encompass(rectToFitUnselected,
                            ge.ChangeCoordinatesTo(ContentContainer, ge.Rect()));
                    }
                }
            }

            // Use selection only if possible, otherwise unselected. Failing both, use original content container rect
            Vector3 frameScaling;
            Vector3 frameTranslation;
            if (reachedFirstSelected) {
                CalculateFrameTransform(rectToFitSelected, layout, k_FrameBorder, out frameTranslation,
                    out frameScaling);
            } else if (reachedFirstUnselected) {
                CalculateFrameTransform(rectToFitUnselected, layout, k_FrameBorder, out frameTranslation,
                    out frameScaling);
            } else {
                // Note: rectToFitSelected will just be the container rect
                CalculateFrameTransform(rectToFitSelected, layout, k_FrameBorder, out frameTranslation,
                    out frameScaling);
            }

            // Update transform
            Matrix4x4.TRS(frameTranslation, Quaternion.identity, frameScaling);
            UpdateViewTransform(frameTranslation, frameScaling);
        }

        private float ZoomRequiredToFrameRect(Rect rectToFit, Rect clientRect, int border) {
            // bring slightly smaller screen rect into GUI space
            Rect screenRect = new() { xMin = border, xMax = clientRect.width - border, yMin = border, yMax = clientRect.height - border };
            Rect identity = GUIUtility.ScreenToGUIRect(screenRect);
            return Math.Min(identity.width / rectToFit.width, identity.height / rectToFit.height);
        }

        public void CalculateFrameTransform(Rect rectToFit, Rect clientRect, int border, out Vector3 frameTranslation,
            out Vector3 frameScaling) {
            // measure zoom level necessary to fit the canvas rect into the screen rect
            float zoomLevel = ZoomRequiredToFrameRect(rectToFit, clientRect, border);

            // clamp
            zoomLevel = Mathf.Clamp(zoomLevel, MinScale, MaxScale);

            Matrix4x4 transformMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new(zoomLevel, zoomLevel, 1.0f));
            Vector2 edge = new(clientRect.width, clientRect.height);
            Vector2 origin = new(0, 0);

            Rect r = new() { min = origin, max = edge };

            Vector3 parentScale = new(transformMatrix.GetColumn(0).magnitude, transformMatrix.GetColumn(1).magnitude, transformMatrix.GetColumn(2).magnitude);
            Vector2 offset = r.center - rectToFit.center * parentScale.x;

            // Update output values before leaving
            frameTranslation = new(offset.x, offset.y, 0.0f);
            frameScaling = parentScale;
        }
        #endregion

    }
}