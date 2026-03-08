using Godot;
using System;
using SakugaEngine.Game;

namespace SakugaEngine.Scenes
{
    public partial class FightSceneController : Node
    {
        private const int DuplicateErrorLogInterval = 60;

        [Export]
        public NodePath GameManagerPath;

        private GameManager gm;
        private double accumulator = 0.0;
        private double tickInterval = 1.0 / Global.TicksPerSecond;
        private bool halted;
        private string lastErrorSignature = string.Empty;
        private int duplicateErrorCount;

        public override void _Ready()
        {
            gm = ResolveGameManager();
            if (gm == null)
            {
                GD.PrintErr("[FightSceneController] Could not find GameManager. Assign `GameManagerPath` or name your GameManager node 'GameManager'.");
                SetPhysicsProcess(false);
                return;
            }

            if (gm.InputSize == 0)
                gm.InputSize = 2;
            gm.Setup();

            if (gm.HasFatalRuntimeError)
            {
                HaltStepLoop($"setup failed: {gm.FatalRuntimeReason}");
            }
        }

        private GameManager ResolveGameManager()
        {
            if (GameManagerPath != null && GameManagerPath.ToString() != string.Empty)
            {
                var fromPath = GetNodeOrNull<GameManager>(GameManagerPath);
                if (fromPath != null) return fromPath;
            }

            // Try to find a GameManager in the scene tree by walking children
            return FindGameManager(GetTree().Root);
        }

        private GameManager FindGameManager(Node n)
        {
            if (n is GameManager gmNode) return gmNode;
            foreach (var child in n.GetChildren())
            {
                if (child is Node cn)
                {
                    var res = FindGameManager(cn);
                    if (res != null) return res;
                }
            }
            return null;
        }

        public override void _PhysicsProcess(double delta)
        {
            if (gm == null || halted) return;

            if (gm.HasFatalRuntimeError)
            {
                HaltStepLoop(gm.FatalRuntimeReason);
                return;
            }

            accumulator += delta;
            while (accumulator >= tickInterval)
            {
                Step();
                accumulator -= tickInterval;
            }
        }

        private void Step()
        {
            string phase = "prepare";

            try
            {
                int players = 2;
                int inputSize = (int)(gm.InputSize > 0 ? gm.InputSize : 2);

                phase = "read_inputs";
                byte[] combined = new byte[inputSize * players];
                for (int p = 0; p < players; p++)
                {
                    var local = gm.GetLocalInput(p, inputSize);
                    if (local == null) continue;
                    Array.Copy(local, 0, combined, p * inputSize, Math.Min(local.Length, inputSize));
                }

                phase = "simulate";
                gm.GameLoop(combined);
                if (gm.HasFatalRuntimeError)
                {
                    HaltStepLoop(gm.FatalRuntimeReason);
                    return;
                }

                phase = "render";
                gm.Render();
                if (gm.HasFatalRuntimeError)
                {
                    HaltStepLoop(gm.FatalRuntimeReason);
                    return;
                }
            }
            catch (Exception e)
            {
                ReportStepException(phase, e);
            }
        }

        private void HaltStepLoop(string reason)
        {
            if (halted) return;

            halted = true;
            SetPhysicsProcess(false);
            GD.PrintErr($"[FightSceneController] Step loop halted: {reason}");
        }

        private void ReportStepException(string phase, Exception e)
        {
            string signature = $"{phase}:{e.GetType().FullName}:{e.Message}";
            if (signature == lastErrorSignature)
            {
                duplicateErrorCount++;
                if (duplicateErrorCount % DuplicateErrorLogInterval == 0)
                {
                    GD.PrintErr($"[FightSceneController] Repeated step error ({duplicateErrorCount}x): {signature}");
                }
                return;
            }

            lastErrorSignature = signature;
            duplicateErrorCount = 0;
            GD.PrintErr($"[FightSceneController] Error during Step phase '{phase}': {e}");
        }
    }
}
