using Godot;
using System;

namespace SakugaEngine.UI
{
    public partial class StageSelectButton : TextureRect
    {
        [Signal] public delegate void OnPressedEventHandler(StageSelectButton button);

        [Export] private float hoverScale = 1.1f;
        [Export] private float normalScale = 0.5f; // Matches default scale in tscn
        [Export] private float scaleSpeed = 10.0f;

        private bool isHovered;

        public int Index { get; set; }

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Stop;
        }

        public bool IsHovered
        {
            get => isHovered;
            set
            {
                if (isHovered != value)
                {
                    isHovered = value;
                }
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
            {
                GD.Print($"StageButton {Index} Clicked");
                EmitSignal(SignalName.OnPressed, this);
            }
            else if (@event is InputEventScreenTouch touchEvent && touchEvent.Pressed)
            {
                GD.Print($"StageButton {Index} Touched");
                EmitSignal(SignalName.OnPressed, this);
            }
        }

        public override void _Process(double delta)
        {
            float targetScale = IsHovered ? hoverScale : normalScale;
            float currentScale = Scale.X;
            float newScale = Mathf.Lerp(currentScale, targetScale, (float)delta * scaleSpeed);
            Scale = new Vector2(newScale, newScale);
        }
    }
}
