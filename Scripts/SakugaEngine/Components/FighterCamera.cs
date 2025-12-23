using Godot;
using System;

namespace SakugaEngine
{
    public partial class FighterCamera : Camera3D
    {
        //private Listener audioListener;

        [Export] public bool isCinematic;
        [Export] public Vector2 minBounds = new Vector2(-5.5f, 1.25f), maxBounds = new Vector2(5.50f, 10f);
        //public int limitPlayersDistance = 600;
        [Export] public Vector2 minOffset = new Vector2(-4f, 1.2f), maxOffset = new Vector2(-5f, 1.55f);
        [Export] public float minSmoothDistance = 4;
        [Export] public float minDistance = 4f, maxDistance = 5.5f;
        [Export] public float boundsAdditionalNear = 2.3f, boundsAdditionalFar = 2.95f;

        private Camera3D charCam;

        private SceneTreeTween introTween;

        const float DELTA = 10f / Global.TicksPerSecond;

        public override void _Ready()
        {
            charCam = GetNode<Camera3D>("../CanvasLayer/ViewportContainer/Viewport_Foreground/CharacterCamera");
            //audioListener = GetNode<Listener>("Listener");
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            SyncCharacterCamera();
        }

        public void UpdateCamera(SakugaFighter player1, SakugaFighter player2)
        {
            if (player1 == null || player2 == null) return;

            Vector3 _p1Position = Global.ToScaledVector3(player1.Body.FixedPosition);
            Vector3 _p2Position = Global.ToScaledVector3(player2.Body.FixedPosition);

            bool canSmooth = Mathf.Abs(_p2Position.X - _p1Position.X) > minSmoothDistance;

            float playerDistance = Mathf.Clamp(Mathf.Abs(_p2Position.X - _p1Position.X), minDistance, maxDistance);
            float pl = (playerDistance - minDistance) / (maxDistance - minDistance);
            float FinalYOffset = Mathf.Lerp(minOffset.Y, maxOffset.Y, pl);
            float FinalZOffset = Mathf.Lerp(minOffset.X, maxOffset.X, pl);

            float finalCamY = 0;
            if (Mathf.Max(_p1Position.Y, _p2Position.Y) >= FinalYOffset)
                finalCamY = Mathf.Max(_p1Position.Y, _p2Position.Y);
            else
                finalCamY = FinalYOffset;

            float actualCenter = (_p1Position.X + _p2Position.X) / 2;
            Vector3 newCamPosition = new Vector3(actualCenter, finalCamY, 0);

            float BoundsAdd = Mathf.Lerp(boundsAdditionalNear, boundsAdditionalFar, pl);
            Position = new Vector3(
                Mathf.Lerp(Position.X, newCamPosition.X, DELTA),
                Mathf.Lerp(Position.Y, newCamPosition.Y, DELTA),
                0);
            Position = new Vector3(
                Mathf.Clamp(Position.X, minBounds.X + BoundsAdd, maxBounds.X - BoundsAdd),
                Mathf.Clamp(Position.Y, minBounds.Y, maxBounds.Y),
                -FinalZOffset);
            
            charCam.GlobalTransform = GlobalTransform;
            charCam.Fov = Fov;

            //audioListener.GlobalTranslation = new Vector3(Position.X, Position.Y, 0);
        }

        public Vector3 CalculateFollowPosition(SakugaFighter player1, SakugaFighter player2)
        {
            if (player1 == null || player2 == null) return Position;

            Vector3 _p1Position = Global.ToScaledVector3(player1.Body.FixedPosition);
            Vector3 _p2Position = Global.ToScaledVector3(player2.Body.FixedPosition);

            float playerDistance = Mathf.Clamp(Mathf.Abs(_p2Position.X - _p1Position.X), minDistance, maxDistance);
            float pl = (playerDistance - minDistance) / (maxDistance - minDistance);
            float FinalYOffset = Mathf.Lerp(minOffset.Y, maxOffset.Y, pl);
            float FinalZOffset = Mathf.Lerp(minOffset.X, maxOffset.X, pl);

            float finalCamY = Mathf.Max(Mathf.Max(_p1Position.Y, _p2Position.Y), FinalYOffset);
            float actualCenter = (_p1Position.X + _p2Position.X) / 2;

            float BoundsAdd = Mathf.Lerp(boundsAdditionalNear, boundsAdditionalFar, pl);
            Vector3 targetPosition = new Vector3(
                Mathf.Clamp(actualCenter, minBounds.X + BoundsAdd, maxBounds.X - BoundsAdd),
                Mathf.Clamp(finalCamY, minBounds.Y, maxBounds.Y),
                -FinalZOffset);

            return targetPosition;
        }

        public SceneTreeTween PlayIntroDolly(Vector3 targetPosition, float duration)
        {
            introTween?.Kill();

            Vector3 startPosition = targetPosition + new Vector3(-0.5f, 3.25f, -4.5f);
            Vector3 midPosition = targetPosition + new Vector3(0f, 2f, -2.5f);
            Position = startPosition;

            introTween = CreateTween();
            introTween.TweenProperty(this, "position", midPosition, duration * 0.6f)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
            introTween.TweenProperty(this, "position", targetPosition, duration * 0.4f)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
            introTween.TweenCallback(Callable.From(() => introTween = null));

            return introTween;
        }

        private void SyncCharacterCamera()
        {
            if (charCam == null) return;

            charCam.GlobalTransform = GlobalTransform;
            charCam.Fov = Fov;
        }
    }
}
