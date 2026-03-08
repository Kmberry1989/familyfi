using Godot;
using System;
using System.Collections.Generic;

namespace SakugaEngine.UI
{
    public partial class MobileControls : Control
    {
        private sealed class TouchSample
        {
            public string Action;
            public Vector2 StartPosition;
            public Vector2 LastPosition;
            public bool StartedOnActionSide;
        }

        private readonly Dictionary<int, TouchSample> _touchSamples = new Dictionary<int, TouchSample>();

        [ExportCategory("Regions")]
        [Export] public Control DPadRegion;
        [Export] public Control ActionRegion;

        [ExportCategory("Buttons")]
        [Export] public Control DPadUp;
        [Export] public Control DPadDown;
        [Export] public Control DPadLeft;
        [Export] public Control DPadRight;

        [Export] public Control ButtonA;
        [Export] public Control ButtonB;
        [Export] public Control ButtonC;
        [Export] public Control ButtonD;

        [ExportCategory("Meta Buttons")]
        [Export] public Control ButtonStart;
        [Export] public Control ButtonBack;
        [Export] public Control ButtonSelect;

        [ExportCategory("Gestures")]
        [Export] public int DoubleTapWindowMs = 260;
        [Export] public float SwipeThreshold = 75.0f;
        [Export] public int TransientFrameDuration = 2;

        private ushort _currentInputState;
        private ushort _transientInputState;
        private int _transientFramesRemaining;
        private long _lastLeftTapTimeMs = -10_000;
        private long _lastRightTapTimeMs = -10_000;

        public override void _Ready()
        {
            Input.UseAccumulatedInput = false;
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventScreenTouch touchEvent)
            {
                if (touchEvent.Pressed)
                {
                    HandleTouchStart(touchEvent.Index, touchEvent.Position);
                }
                else
                {
                    HandleTouchEnd(touchEvent.Index);
                }
            }
            else if (@event is InputEventScreenDrag dragEvent)
            {
                // Optional: Handle D-Pad sliding
                HandleTouchDrag(dragEvent.Index, dragEvent.Position);
            }
        }

        private void HandleTouchStart(int index, Vector2 position)
        {
            TouchSample sample = new TouchSample
            {
                StartPosition = position,
                LastPosition = position,
                StartedOnActionSide = IsActionSidePosition(position),
                Action = GetActionAtPosition(position)
            };

            _touchSamples[index] = sample;
            TryQueueDashFromDoubleTap(sample.Action);
        }

        private void HandleTouchDrag(int index, Vector2 position)
        {
            if (!_touchSamples.TryGetValue(index, out TouchSample sample)) return;

            sample.LastPosition = position;
            sample.Action = GetActionAtPosition(position);
        }

        private void HandleTouchEnd(int index)
        {
            if (!_touchSamples.TryGetValue(index, out TouchSample sample)) return;

            TryQueueTauntFromSwipe(sample);
            _touchSamples.Remove(index);
        }

        private void TryQueueDashFromDoubleTap(string action)
        {
            long now = (long)Time.GetTicksMsec();

            if (action == "LEFT")
            {
                if (now - _lastLeftTapTimeMs <= DoubleTapWindowMs)
                    QueueTransientInputs((ushort)(Global.INPUT_DASH | Global.INPUT_LEFT));
                _lastLeftTapTimeMs = now;
            }
            else if (action == "RIGHT")
            {
                if (now - _lastRightTapTimeMs <= DoubleTapWindowMs)
                    QueueTransientInputs((ushort)(Global.INPUT_DASH | Global.INPUT_RIGHT));
                _lastRightTapTimeMs = now;
            }
        }

        private void TryQueueTauntFromSwipe(TouchSample sample)
        {
            if (sample == null || !sample.StartedOnActionSide) return;

            Vector2 delta = sample.LastPosition - sample.StartPosition;
            if (delta.Y > -SwipeThreshold) return;
            if (Mathf.Abs(delta.Y) <= Mathf.Abs(delta.X)) return;

            QueueTransientInputs((ushort)Global.INPUT_TAUNT);
        }

        private bool IsActionSidePosition(Vector2 position)
        {
            if (IsPointInControl(ActionRegion, position)) return true;

            return IsPointInControl(ButtonA, position) ||
                   IsPointInControl(ButtonB, position) ||
                   IsPointInControl(ButtonC, position) ||
                   IsPointInControl(ButtonD, position) ||
                   IsPointInControl(ButtonStart, position) ||
                   IsPointInControl(ButtonBack, position) ||
                   IsPointInControl(ButtonSelect, position);
        }

        private string GetActionAtPosition(Vector2 position)
        {
            if (IsPointInControl(DPadUp, position)) return "UP";
            if (IsPointInControl(DPadDown, position)) return "DOWN";
            if (IsPointInControl(DPadLeft, position)) return "LEFT";
            if (IsPointInControl(DPadRight, position)) return "RIGHT";

            if (IsPointInControl(ButtonA, position)) return "A";
            if (IsPointInControl(ButtonB, position)) return "B";
            if (IsPointInControl(ButtonC, position)) return "C";
            if (IsPointInControl(ButtonD, position)) return "D";

            if (IsPointInControl(ButtonStart, position)) return "START";
            if (IsPointInControl(ButtonBack, position)) return "BACK";
            if (IsPointInControl(ButtonSelect, position)) return "SELECT";

            return null;
        }

        private bool IsPointInControl(Control control, Vector2 point)
        {
            if (control == null || !control.IsVisibleInTree()) return false;
            return control.GetGlobalRect().HasPoint(point);
        }

        private void QueueTransientInputs(ushort inputs)
        {
            _transientInputState |= inputs;
            _transientFramesRemaining = Mathf.Max(_transientFramesRemaining, Mathf.Max(1, TransientFrameDuration));
        }

        private ushort BuildHeldInputState()
        {
            ushort heldState = 0;

            foreach (TouchSample sample in _touchSamples.Values)
            {
                if (sample == null || string.IsNullOrEmpty(sample.Action)) continue;

                switch (sample.Action)
                {
                    case "UP": heldState |= (ushort)Global.INPUT_UP; break;
                    case "DOWN": heldState |= (ushort)Global.INPUT_DOWN; break;
                    case "LEFT": heldState |= (ushort)Global.INPUT_LEFT; break;
                    case "RIGHT": heldState |= (ushort)Global.INPUT_RIGHT; break;
                    case "A": heldState |= (ushort)Global.INPUT_FACE_A; break;
                    case "B": heldState |= (ushort)Global.INPUT_FACE_B; break;
                    case "C": heldState |= (ushort)Global.INPUT_FACE_C; break;
                    case "D": heldState |= (ushort)Global.INPUT_FACE_D; break;
                    case "START": heldState |= (ushort)Global.INPUT_MENU; break;
                    case "BACK": heldState |= (ushort)Global.INPUT_BACK; break;
                    case "SELECT": heldState |= (ushort)Global.INPUT_BACK; break;
                    case "DASH": heldState |= (ushort)Global.INPUT_DASH; break;
                    case "TAUNT": heldState |= (ushort)Global.INPUT_TAUNT; break;
                }
            }

            return heldState;
        }

        public ushort GetInput()
        {
            _currentInputState = BuildHeldInputState();

            if (_transientFramesRemaining > 0)
            {
                _currentInputState |= _transientInputState;
                _transientFramesRemaining--;
                if (_transientFramesRemaining == 0)
                    _transientInputState = 0;
            }

            return _currentInputState;
        }
    }
}
