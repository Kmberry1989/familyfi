using Godot;
using System;

namespace SakugaEngine.UI
{
    public partial class CharSelectButton : TextureRect
    {
        [Signal] public delegate void OnPressedEventHandler(CharSelectButton button);

        [Export] private TextureRect hoverBackground;
        [Export] private TextureRect selectionFlash;
        [Export] private float hoverScale = 1.1f;
        [Export] private float normalScale = 1.0f;
        [Export] private float scaleSpeed = 10.0f;

        private bool isHovered;
        private bool isSelected;

        public int Index { get; set; }

        public bool IsHovered
        {
            get => isHovered;
            set
            {
                if (isHovered != value)
                {
                    isHovered = value;
                    if (hoverBackground != null) hoverBackground.Visible = value;
                }
            }
        }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                isSelected = value;
                if (isSelected && selectionFlash != null)
                {
                    selectionFlash.Modulate = new Color(1, 1, 1, 1);
                    selectionFlash.Visible = true;
                }
                else if (!isSelected && selectionFlash != null)
                {
                    selectionFlash.Visible = false;
                }
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
            {
                EmitSignal(SignalName.OnPressed, this);
            }
            else if (@event is InputEventScreenTouch touchEvent && touchEvent.Pressed)
            {
                EmitSignal(SignalName.OnPressed, this);
            }
        }

        public override void _Process(double delta)
        {
            float targetScale = IsHovered ? hoverScale : normalScale;
            float currentScale = Scale.X;
            float newScale = Mathf.Lerp(currentScale, targetScale, (float)delta * scaleSpeed);
            Scale = new Vector2(newScale, newScale);

            if (selectionFlash != null && selectionFlash.Visible)
            {
                // Fade out flash
                Color c = selectionFlash.Modulate;
                c.A -= (float)delta * 2.0f;
                selectionFlash.Modulate = c;
                if (c.A <= 0) selectionFlash.Visible = false;
            }
        }
    }
}
