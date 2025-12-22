using Godot;
using System.Collections.Generic;

namespace SakugaEngine.UI
{
    public partial class MobileControls : Control
    {
        // Dictionary to track which action is held by which touch index
        private Dictionary<int, string> _activeTouches = new Dictionary<int, string>();

        // Configuration for buttons
        [Export] public Control DPadUp;
        [Export] public Control DPadDown;
        [Export] public Control DPadLeft;
        [Export] public Control DPadRight;

        [Export] public Control ButtonA;
        [Export] public Control ButtonB;
        [Export] public Control ButtonC;
        [Export] public Control ButtonD;

        [Export] public Control ButtonStart;
        [Export] public Control ButtonSelect;

        private ushort _currentInputState;

        public override void _Ready()
        {
            // Ensure multi-touch is enabled
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
            string action = GetActionAtPosition(position);
            if (!string.IsNullOrEmpty(action))
            {
                if (_activeTouches.ContainsKey(index))
                    _activeTouches[index] = action;
                else
                    _activeTouches.Add(index, action);
            }
        }

        private void HandleTouchDrag(int index, Vector2 position)
        {
            // For now, simple update similar to start. 
            // Better D-Pad logic often involves checking distance from center of D-Pad anchor.
            // But checking button bounds is a good start.
            string action = GetActionAtPosition(position);

            if (_activeTouches.ContainsKey(index))
            {
                string previousAction = _activeTouches[index];
                if (previousAction != action)
                {
                    _activeTouches[index] = action;
                }
            }
        }

        private void HandleTouchEnd(int index)
        {
            if (_activeTouches.ContainsKey(index))
            {
                _activeTouches.Remove(index);
            }
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
            if (IsPointInControl(ButtonSelect, position)) return "SELECT";

            return null;
        }

        private bool IsPointInControl(Control control, Vector2 point)
        {
            if (control == null || !control.IsVisibleInTree()) return false;
            return control.GetGlobalRect().HasPoint(point);
        }

        public ushort GetInput()
        {
            _currentInputState = 0;

            foreach (var action in _activeTouches.Values)
            {
                switch (action)
                {
                    case "UP": _currentInputState |= (ushort)Global.INPUT_UP; break;
                    case "DOWN": _currentInputState |= (ushort)Global.INPUT_DOWN; break;
                    case "LEFT": _currentInputState |= (ushort)Global.INPUT_LEFT; break;
                    case "RIGHT": _currentInputState |= (ushort)Global.INPUT_RIGHT; break;
                    case "A": _currentInputState |= (ushort)Global.INPUT_FACE_A; break;
                    case "B": _currentInputState |= (ushort)Global.INPUT_FACE_B; break;
                    case "C": _currentInputState |= (ushort)Global.INPUT_FACE_C; break;
                    case "D": _currentInputState |= (ushort)Global.INPUT_FACE_D; break;
                    // Map Start/Select to appropriate flags if they exist
                    case "START": _currentInputState |= (ushort)Global.INPUT_MENU; break;
                        // case "SELECT": _currentInputState |= (ushort)Global.INPUT_SELECT; break; 
                }
            }

            return _currentInputState;
        }
    }
}
