using Godot;
using System.IO;
using System;
using PleaseResync;
using SakugaEngine.Resources;
using SakugaEngine.Collision;
using SakugaEngine.UI;
using System.Text;
using System.Collections.Generic;

namespace SakugaEngine.Game
{
    public partial class GameManager : Node, IGameState
    {
        [Export] private GameMonitor Monitor;
        [Export] public FighterList fightersList;
        [Export] public StageList stagesList;
        [Export] public BGMList songsList;
        [Export] public int player1Character;
        [Export] public int player2Character;
        [Export] public int selectedStage;
        [Export] public int selectedBGM;
        [Export] private CanvasLayer FighterUI;
        [Export] private FighterCamera Camera;
        [Export] private AudioStreamPlayer BGMSource;
        [Export] private MobileControls TouchInput;
        [Export] Label SeedViewer;
        [Export] private bool enableSharedAnimationInjection = false;
        public uint InputSize;

        private List<SakugaNode> Nodes;
        private SakugaFighter[] Fighters;
        private PhysicsWorld World;

        private HealthHUD healthHUD;
        private MetersHUD metersHUD;

        private Node3D stageInstance;
        private bool inputsLocked = true;
        private bool aiMovementLocked = true;
        private bool cameraFollowLocked;

        private Control fightBanner;
        private Label fightLabel;

        private const float fightFlashTime = 0.75f;
        private const float introDollyDuration = 1.0f;
        private const float aiIntroAdvanceDuration = 1.5f;
        private const float aiIntroAdvanceStepDuration = 0.35f;
        private const float aiIntroAdvancePauseDuration = 0.25f;

        private int Frame = 0;
        private int generatedSeed = 0;
        private int finalSeed = 0;

        Vector3I randomTest = new();

        private bool aiIntroAdvanceActive;
        private bool aiIntroAdvanceMoving;
        private bool runtimeReady;
        private bool fatalRuntimeError;
        private string fatalRuntimeReason = string.Empty;
        private readonly HashSet<string> oneShotWarnings = new HashSet<string>();

        public bool HasFatalRuntimeError => fatalRuntimeError;
        public string FatalRuntimeReason => fatalRuntimeReason;
        public bool IsRuntimeReady => runtimeReady && !fatalRuntimeError;

        private void WarnOnce(string key, string message)
        {
            if (!oneShotWarnings.Add(key)) return;
            GD.PrintErr(message);
        }

        private void MarkFatalRuntime(string context, IEnumerable<string> issues)
        {
            if (fatalRuntimeError) return;

            fatalRuntimeError = true;
            runtimeReady = false;
            fatalRuntimeReason = $"{context}: {string.Join(", ", issues)}";
            GD.PrintErr($"GameManager fatal runtime dependency error ({context}): {string.Join(", ", issues)}.");
        }

        private bool ValidateRuntimeDependencies(string context, bool logIssues, bool markFatal)
        {
            List<string> missing = new List<string>();

            if (World == null) missing.Add(nameof(World));
            if (Monitor == null) missing.Add(nameof(Monitor));
            if (Camera == null) missing.Add(nameof(Camera));
            if (healthHUD == null) missing.Add(nameof(healthHUD));
            if (metersHUD == null) missing.Add(nameof(metersHUD));

            if (Nodes == null) missing.Add(nameof(Nodes));

            if (Fighters == null || Fighters.Length < 2)
            {
                missing.Add("Fighters[2]");
            }
            else
            {
                for (int i = 0; i < Fighters.Length; i++)
                {
                    SakugaFighter fighter = Fighters[i];
                    if (fighter == null)
                    {
                        missing.Add($"Fighters[{i}]");
                        continue;
                    }
                    if (fighter.Body == null) missing.Add($"Fighters[{i}].Body");
                    if (fighter.Inputs == null) missing.Add($"Fighters[{i}].Inputs");
                    if (fighter.Animator == null) missing.Add($"Fighters[{i}].Animator");
                }
            }

            if (missing.Count == 0)
            {
                runtimeReady = true;
                return true;
            }

            runtimeReady = false;

            if (logIssues)
            {
                WarnOnce($"runtime_missing_{context}", $"GameManager runtime validation failed ({context}): {string.Join(", ", missing)}");
            }

            if (markFatal)
            {
                MarkFatalRuntime(context, missing);
            }

            return false;
        }

        private bool EnsureRuntimeReady(string context, bool markFatal)
        {
            if (fatalRuntimeError) return false;
            if (runtimeReady) return true;
            return ValidateRuntimeDependencies(context, true, markFatal);
        }

        public override void _Ready()
        {
            healthHUD = FighterUI?.GetNodeOrNull<HealthHUD>("GameHUD_Background");
            metersHUD = FighterUI?.GetNodeOrNull<MetersHUD>("GameHUD_Foreground");
            fightBanner = FighterUI?.GetNodeOrNull<Control>("FightBanner");
            fightLabel = fightBanner?.GetNodeOrNull<Label>("FightLabel");
            if (fightBanner != null) fightBanner.Visible = false;
            Nodes ??= new List<SakugaNode>();

            if (FighterUI == null)
                WarnOnce("missing_fighter_ui", "GameManager: FighterUI is null.");
            if (healthHUD == null)
                WarnOnce("missing_health_hud", "GameManager: Could not resolve GameHUD_Background from FighterUI.");
            if (metersHUD == null)
                WarnOnce("missing_meters_hud", "GameManager: Could not resolve GameHUD_Foreground from FighterUI.");

            // Try to locate the optional GDScript Input Manager addon and connect signals
            TryConnectAddonInputManager();
        }

        private Node addonInputNode = null;
        private bool addonInputActive = false;
        // Per-player raw input cache populated by the addon (low 8 bits, high 8 bits available)
        private ushort[] addonRawInputs = new ushort[2];

        private void TryConnectAddonInputManager()
        {
            // Search the entire scene tree root for the addon script (addons/input_manager/input_manager.gd)
            Node root = GetTree().Root as Node;
            addonInputNode = FindAddonInputNode(root);
            if (addonInputNode == null) return;

            // Found a candidate addon node; proceed to connect

            // Connect signals exposed by the addon
            try
            {
                addonInputNode.Connect("on_action_button", new Callable(this, nameof(OnAddonActionButton)));
                addonInputNode.Connect("on_action_stick", new Callable(this, nameof(OnAddonActionStick)));
                addonInputNode.Connect("on_action_trigger", new Callable(this, nameof(OnAddonActionTrigger)));
                addonInputActive = true;
                GD.Print("GameManager: Connected to addon InputManager at " + addonInputNode.GetPath());
            }
            catch (System.Exception e)
            {
                GD.PrintErr("GameManager: Failed to connect to addon InputManager: " + e.Message);
            }
        }

        private Node FindAddonInputNode(Node n)
        {
            if (n == null) return null;
            // Check this node
                // Detect addon by presence of the expected signals
                if (n.HasSignal("on_action_button") && n.HasSignal("on_action_stick") && n.HasSignal("on_action_trigger"))
                    return n;

            foreach (var child in n.GetChildren())
            {
                if (child is Node cn)
                {
                    var found = FindAddonInputNode(cn);
                    if (found != null) return found;
                }
            }

            return null;
        }

        // Signals from addon: action name and pressed state
        public void OnAddonActionButton(Variant actionName, Variant pressed)
        {
            string action = actionName.ToString();
            bool isPressed = (bool)pressed;
            ApplyAddonButtonAction(action, isPressed);
        }

        // Signals from addon: action name and Vector2 stick value
        public void OnAddonActionStick(Variant actionName, Variant value)
        {
            string action = actionName.ToString();
            Vector2 v = (Vector2)value;
            ApplyAddonStickAction(action, v);
        }

        // Signals from addon: action name and float trigger value
        public void OnAddonActionTrigger(Variant actionName, Variant value)
        {
            string action = actionName.ToString();
            float v = (float)value;
            ApplyAddonTriggerAction(action, v);
        }

        private void ApplyAddonButtonAction(string action, bool pressed)
        {
            // Expect actions named like "k1_face_a", "k2_left" etc.
            int player = -1;
            if (action.StartsWith("k1_")) player = 0;
            else if (action.StartsWith("k2_")) player = 1;
            if (player == -1) return; // ignore unprefixed actions for now

            string suffix = action[(action.IndexOf('_') + 1)..];

            int bit = 0;
            switch (suffix)
            {
                case "up": bit = Global.INPUT_UP; break;
                case "down": bit = Global.INPUT_DOWN; break;
                case "left": bit = Global.INPUT_LEFT; break;
                case "right": bit = Global.INPUT_RIGHT; break;
                case "face_a": bit = Global.INPUT_FACE_A; break;
                case "face_b": bit = Global.INPUT_FACE_B; break;
                case "face_c": bit = Global.INPUT_FACE_C; break;
                case "face_d": bit = Global.INPUT_FACE_D; break;
                default: return;
            }

            if (pressed)
                addonRawInputs[player] |= (ushort)bit;
            else
                addonRawInputs[player] &= unchecked((ushort)~(ushort)bit);
        }

        private void ApplyAddonStickAction(string action, Vector2 v)
        {
            int player = -1;
            if (action.StartsWith("k1_")) player = 0;
            else if (action.StartsWith("k2_")) player = 1;
            if (player == -1) return;

            // Expect suffix like "left_stick" or "right_stick"; we only care about directional axes
            float dead = 0.5f;
            // Horizontal
            if (v.X > dead) { addonRawInputs[player] |= (ushort)Global.INPUT_RIGHT; addonRawInputs[player] &= unchecked((ushort)~(ushort)Global.INPUT_LEFT); }
            else if (v.X < -dead) { addonRawInputs[player] |= (ushort)Global.INPUT_LEFT; addonRawInputs[player] &= unchecked((ushort)~(ushort)Global.INPUT_RIGHT); }
            else { addonRawInputs[player] &= unchecked((ushort)~(ushort)Global.INPUT_LEFT); addonRawInputs[player] &= unchecked((ushort)~(ushort)Global.INPUT_RIGHT); }
            // Vertical
            if (v.Y > dead) { addonRawInputs[player] |= (ushort)Global.INPUT_DOWN; addonRawInputs[player] &= unchecked((ushort)~(ushort)Global.INPUT_UP); }
            else if (v.Y < -dead) { addonRawInputs[player] |= (ushort)Global.INPUT_UP; addonRawInputs[player] &= unchecked((ushort)~(ushort)Global.INPUT_DOWN); }
            else { addonRawInputs[player] &= unchecked((ushort)~(ushort)Global.INPUT_UP); addonRawInputs[player] &= unchecked((ushort)~(ushort)Global.INPUT_DOWN); }
        }

        private void ApplyAddonTriggerAction(string action, float v)
        {
            // Map triggers to face buttons if action is prefixed with k1_/k2_
            int player = -1;
            if (action.StartsWith("k1_")) player = 0;
            else if (action.StartsWith("k2_")) player = 1;
            if (player == -1) return;

            // Simple mapping: if trigger pressed substantially, set FACE_A
            if (v > 0.5f) addonRawInputs[player] |= (ushort)Global.INPUT_FACE_A;
            else addonRawInputs[player] &= unchecked((ushort)~(ushort)Global.INPUT_FACE_A);
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            if (fatalRuntimeError) return;
            if (Fighters == null) return;
            if (Monitor == null) return;

            if (BGMSource != null && !BGMSource.Playing) BGMSource.Play();
            else if (BGMSource == null) WarnOnce("missing_bgm_source", "GameManager: BGMSource is null.");

            if (SeedViewer != null) SeedViewer.Text = finalSeed.ToString();
            else WarnOnce("missing_seed_viewer", "GameManager: SeedViewer label is null.");

            if (Input.IsActionJustPressed("toggle_hitboxes"))
                Global.ShowHitboxes = !Global.ShowHitboxes;
        }

        public void SetBGM()
        {
            if (BGMSource != null &&
                songsList != null &&
                songsList.elements != null &&
                selectedBGM >= 0 &&
                selectedBGM < songsList.elements.Length &&
                !BGMSource.Playing &&
                songsList.elements[selectedBGM].clip != null)
            {
                BGMSource.Stream = songsList.elements[selectedBGM].clip;
            }
        }

        public void Render()
        {
            if (!EnsureRuntimeReady(nameof(Render), true)) return;

            RenderNodes();
            if (!cameraFollowLocked && Camera != null)
                Camera.UpdateCamera(Fighters[0], Fighters[1]);
            if (healthHUD != null)
                healthHUD.UpdateHealthBars(Fighters, Monitor);
            if (metersHUD != null)
                metersHUD.UpdateMeters(Fighters);
        }

        void RenderNodes()
        {
            if (Nodes == null) return;

            foreach (SakugaNode node in Nodes)
            {
                if (node == null) continue;
                node.Render();
            }
        }

        /// <summary>
        /// Generates the base seed to be used for generating the PRNG. The base seed is a non-random 
        /// number generated with a default string and both characters' names.
        /// </summary>
        private void GenerateBaseSeed()
        {
            if (Fighters[0] == null || Fighters[1] == null)
            {
                GD.PrintErr("GenerateBaseSeed: A Fighter is NULL!");
                return;
            }

            string p1Name = "Player1";
            string p2Name = "Player2";

            if (Fighters[0].Profile != null) p1Name = Fighters[0].Profile.FighterName;
            else GD.PrintErr("GenerateBaseSeed: Fighter 0 Profile is NULL!");

            if (Fighters[1].Profile != null) p2Name = Fighters[1].Profile.FighterName;
            else GD.PrintErr("GenerateBaseSeed: Fighter 1 Profile is NULL!");

            string seedText = Global.baseSeed + p1Name + p2Name;
            byte[] seedArray = Encoding.ASCII.GetBytes(seedText);
            generatedSeed = (int)Platform.GetChecksum(seedArray);
        }

        /// <summary>
        /// Generate the PRNG seed with a bunch of everchanging values. 
        /// If the values used are deterministic, the generated seed will be deterministic.
        /// </summary>
        /// <returns>a 32-bit seed number</returns>
        private int CalculateSeed()
        {
            if (Fighters == null || Fighters.Length < 2 || Fighters[0] == null || Fighters[1] == null) return generatedSeed;

            int posX = Fighters[0].Body.FixedPosition.X + Fighters[1].Body.FixedPosition.X;
            int posY = Fighters[0].Body.FixedPosition.Y + Fighters[1].Body.FixedPosition.Y;
            int stateFrame = Fighters[0].Animator.Frame + Fighters[0].Animator.CurrentState + Fighters[1].Animator.Frame + Fighters[1].Animator.CurrentState;
            int monitorClock = Monitor?.Clock ?? 0;
            return generatedSeed + posX + posY + stateFrame + (Frame * Global.SimulationScale) + monitorClock;
        }

        public void Setup()
        {
            oneShotWarnings.Clear();
            runtimeReady = false;
            fatalRuntimeError = false;
            fatalRuntimeReason = string.Empty;

            // Sync with Global Match settings
            player1Character = Global.Match.Player1.selectedCharacter;
            player2Character = Global.Match.Player2.selectedCharacter;
            selectedStage = Global.Match.selectedStage;
            selectedBGM = Global.Match.selectedBGM;

            if (GetChildren().Count > 0)
            {
                foreach (Node child in GetChildren())
                {
                    if (child == FighterUI || child == TouchInput) continue;
                    child.QueueFree();
                }
            }

            Frame = 0;

            stageInstance = null;
            inputsLocked = true;
            aiMovementLocked = true;
            cameraFollowLocked = false;
            aiIntroAdvanceActive = false;
            aiIntroAdvanceMoving = false;

            CreateStage(selectedStage);

            World = new PhysicsWorld();
            if (Nodes == null) Nodes = new List<SakugaNode>();
            Nodes.Clear();
            Fighters = new SakugaFighter[2];

            CreateFighter(player1Character, 0);
            CreateFighter(player2Character, 1);

            if (Fighters[0] == null || Fighters[1] == null)
            {
                MarkFatalRuntime(nameof(Setup), new[] { "Failed to create both fighters." });
                return;
            }

            Fighters[0].SetOpponent(Fighters[1]);
            Fighters[1].SetOpponent(Fighters[0]);

            //AI test (select it in a better way later)
            Fighters[0].UseAI = Global.Match.Player1.selectedDevice == -1;
            Fighters[1].UseAI = Global.Match.Player2.selectedDevice == -1;
            bool touchInputAvailable = TouchInput != null && TouchInput.IsVisibleInTree();
            GD.Print($"Setup: P1 Device={Global.Match.Player1.selectedDevice}, P2 Device={Global.Match.Player2.selectedDevice}. Touch Controls Available: {touchInputAvailable}.");

            GenerateBaseSeed();

            if (Monitor != null) Monitor.Initialize(Fighters);
            else GD.PrintErr("GameManager: Monitor is null!");

            if (healthHUD == null && FighterUI != null) healthHUD = FighterUI.GetNodeOrNull<HealthHUD>("GameHUD_Background");
            if (healthHUD != null) healthHUD.Setup(Fighters);
            else GD.PrintErr("GameManager: healthHUD is null! (Check FighterUI connection)");

            if (metersHUD == null && FighterUI != null) metersHUD = FighterUI.GetNodeOrNull<MetersHUD>("GameHUD_Foreground");
            if (metersHUD != null) metersHUD.Setup(Fighters);
            else GD.PrintErr("GameManager: metersHUD is null! (Check FighterUI connection)");

            if (!ValidateRuntimeDependencies(nameof(Setup), true, true))
                return;

            StartIntroSequence();
        }

        public void CreateFighter(int characterIndex, int playerIndex)
        {
            if (fightersList == null || fightersList.elements == null || fightersList.elements.Length == 0)
            {
                MarkFatalRuntime(nameof(CreateFighter), new[] { "fightersList is empty or null." });
                return;
            }
            if (characterIndex < 0 || characterIndex >= fightersList.elements.Length)
            {
                GD.PrintErr($"Invalid Character Index: {characterIndex}! Defaulting to 0.");
                characterIndex = 0;
            }
            if (fightersList.elements[characterIndex].Instance == null)
            {
                MarkFatalRuntime(nameof(CreateFighter), new[] { $"fightersList[{characterIndex}].Instance is null." });
                return;
            }

            Node temp = fightersList.elements[characterIndex].Instance.Instantiate();
            SakugaFighter fighter = temp as SakugaFighter;
            if (fighter == null)
            {
                MarkFatalRuntime(nameof(CreateFighter), new[] { $"Instanced fighter at index {characterIndex} is not a SakugaFighter." });
                return;
            }

            Fighters[playerIndex] = fighter;
            Fighters[playerIndex].Profile = fightersList.elements[characterIndex].Profile;
            AddActor(Fighters[playerIndex]);
            Fighters[playerIndex].Initialize(playerIndex);
            Fighters[playerIndex].SpawnablesSetup(this);
            Fighters[playerIndex].VFXSetup(this);
            InjectSharedAnimations(Fighters[playerIndex]);
        }

        public void CreateStage(int stageIndex)
        {
            if (stageIndex < 0 || stageIndex >= stagesList.elements.Length)
            {
                GD.PrintErr($"Invalid Stage Index: {stageIndex}! Defaulting to 0.");
                stageIndex = 0;
            }
            Node temp = stagesList.elements[stageIndex].Instance.Instantiate();
            stageInstance = temp as Node3D;
            AddChild(temp);
        }

        private void PositionFightersAtSpawns()
        {
            if (Fighters == null || Fighters.Length < 2 || Fighters[0] == null || Fighters[1] == null) return;

            Vector2I leftPosition = new(-Global.StartingPosition, 0);
            Vector2I rightPosition = new(Global.StartingPosition, 0);

            if (stageInstance != null)
            {
                Node3D spawnRoot = stageInstance.GetNodeOrNull<Node3D>("SpawnMarkers");
                Vector2I? left = GetSpawnPosition(spawnRoot?.GetNodeOrNull<Node3D>("LeftSpawn"));
                Vector2I? right = GetSpawnPosition(spawnRoot?.GetNodeOrNull<Node3D>("RightSpawn"));

                if (spawnRoot != null)
                {
                    Vector2I? furthestLeft = null;
                    Vector2I? furthestRight = null;

                    foreach (Node child in spawnRoot.GetChildren())
                    {
                        if (child is not Node3D marker) continue;
                        Vector2I? spawnPos = GetSpawnPosition(marker);
                        if (!spawnPos.HasValue) continue;

                        if (!furthestLeft.HasValue || spawnPos.Value.X < furthestLeft.Value.X)
                            furthestLeft = spawnPos;

                        if (!furthestRight.HasValue || spawnPos.Value.X > furthestRight.Value.X)
                            furthestRight = spawnPos;
                    }

                    left ??= furthestLeft;
                    right ??= furthestRight;
                }

                if (left.HasValue) leftPosition = left.Value;
                if (right.HasValue) rightPosition = right.Value;
            }

            Fighters[0].Body.FixedVelocity = Vector2I.Zero;
            Fighters[1].Body.FixedVelocity = Vector2I.Zero;

            Fighters[0].Body.FixedPosition = leftPosition;
            Fighters[1].Body.FixedPosition = rightPosition;

            Fighters[0].ForcePlayerSide(true);
            Fighters[1].ForcePlayerSide(false);

            Fighters[0].PlayNeutralState();
            Fighters[1].PlayNeutralState();
        }

        private static Vector2I? GetSpawnPosition(Node3D spawn)
        {
            if (spawn == null) return null;

            return new Vector2I(
                (int)(spawn.GlobalPosition.X * Global.SimulationScale),
                (int)(spawn.GlobalPosition.Y * Global.SimulationScale)
            );
        }

        private float PlayTaunts()
        {
            float maxDuration = 0.5f;

            for (int i = 0; i < Fighters.Length; i++)
            {
                int tauntIndex = Fighters[i].GetStateIndexByName("Taunt");
                if (tauntIndex >= 0)
                {
                    Fighters[i].Animator.PlayState(tauntIndex, true);
                    Fighters[i].Animator.Frame = -1;
                    maxDuration = Mathf.Max(maxDuration, Fighters[i].GetStateDurationSeconds(tauntIndex));
                }
                else Fighters[i].PlayNeutralState();
            }

            return maxDuration;
        }

        private static ushort GetAdvanceInput(SakugaFighter fighter)
        {
            return (ushort)(fighter.Body.IsLeftSide ? Global.INPUT_RIGHT : Global.INPUT_LEFT);
        }

        private void ShowFightLabel(bool visible)
        {
            if (fightBanner == null) return;

            fightBanner.Visible = visible;
            if (fightLabel != null) fightLabel.Modulate = Colors.White;
        }

        private async void StartIntroSequence()
        {
            if (!EnsureRuntimeReady(nameof(StartIntroSequence), true)) return;
            GD.Print("StartIntroSequence: Begin");
            PositionFightersAtSpawns();

            float tauntDuration = PlayTaunts();
            GD.Print($"StartIntroSequence: Playing Taunts ({tauntDuration}s)");
            await ToSignal(GetTree().CreateTimer(tauntDuration), "timeout");
            GD.Print("StartIntroSequence: Taunts finished");

            Fighters[0].PlayNeutralState();
            Fighters[1].PlayNeutralState();

            Vector3 targetCameraPosition = Camera.CalculateFollowPosition(Fighters[0], Fighters[1]);
            cameraFollowLocked = true;
            GD.Print("StartIntroSequence: Starting Dolly");
            Tween dolly = Camera.PlayIntroDolly(targetCameraPosition, introDollyDuration);
            if (dolly != null)
                await ToSignal(dolly, "finished");
            GD.Print("StartIntroSequence: Dolly finished");

            ShowFightLabel(true);
            await ToSignal(GetTree().CreateTimer(fightFlashTime), "timeout");
            ShowFightLabel(false);

            cameraFollowLocked = false;
            inputsLocked = false;
            GD.Print("StartIntroSequence: Inputs Unlocked!");
            StartHesitantAdvance();
        }

        private async void StartHesitantAdvance()
        {
            aiMovementLocked = false;

            bool hasAI = false;
            foreach (SakugaFighter fighter in Fighters)
            {
                if (fighter != null && fighter.UseAI)
                {
                    hasAI = true;
                    break;
                }
            }

            if (!hasAI) return;

            aiIntroAdvanceActive = true;
            aiIntroAdvanceMoving = true;

            float elapsed = 0f;
            bool steppingForward = true;

            while (elapsed < aiIntroAdvanceDuration && aiIntroAdvanceActive)
            {
                aiIntroAdvanceMoving = steppingForward;
                float window = steppingForward ? aiIntroAdvanceStepDuration : aiIntroAdvancePauseDuration;

                await ToSignal(GetTree().CreateTimer(window), "timeout");

                elapsed += window;
                steppingForward = !steppingForward;
            }

            aiIntroAdvanceActive = false;
        }

        public void AddActor(SakugaNode newNode, bool isPhysicsBody = true)
        {
            if (newNode == null)
            {
                WarnOnce("add_actor_null", "GameManager.AddActor received a null node.");
                return;
            }

            Nodes ??= new List<SakugaNode>();
            AddChild(newNode);
            Nodes.Add(newNode);
            if (isPhysicsBody)
            {
                SakugaActor actor = newNode as SakugaActor;
                if (World == null || actor?.Body == null)
                {
                    WarnOnce("add_actor_missing_body", $"GameManager.AddActor skipped physics registration for '{newNode.Name}'.");
                }
                else
                {
                    World.AddBody(actor.Body);
                }
            }
        }

        public void GameLoop(byte[] playerInput)
        {
            if (!EnsureRuntimeReady(nameof(GameLoop), true)) return;

            int expectedInputSize = (int)(InputSize * Fighters.Length);
            if (playerInput == null || playerInput.Length < expectedInputSize)
            {
                GD.PrintErr($"GameLoop: playerInput length ({playerInput?.Length ?? 0}) is smaller than expected ({expectedInputSize}).");
                return;
            }

            finalSeed = CalculateSeed();
            Global.UpdateRNG(finalSeed);
            Frame++;
            Monitor?.Tick();

            randomTest = new Vector3I(
                Global.RNG.Next(),
                Global.RNG.Next(),
                Global.RNG.Next()
            );

            int center = (Fighters[0].Body.FixedPosition.X + Fighters[1].Body.FixedPosition.X) / 2;

            for (int i = 0; i < Fighters.Length; i++)
            {
                if (Fighters[i].UseAI)
                {
                    if (inputsLocked || aiMovementLocked)
                    {
                        Fighters[i].ParseInputs(0);
                        continue;
                    }

                    if (aiIntroAdvanceActive)
                    {
                        Fighters[i].ParseInputs(aiIntroAdvanceMoving ? GetAdvanceInput(Fighters[i]) : (ushort)0);
                        continue;
                    }

                    if (Fighters[i].Brain != null)
                    {
                        Fighters[i].Brain.SelectCommand();
                        Fighters[i].Brain.UpdateCommand();
                    }
                    // Else: Brain is missing, do nothing (dummy behavior)
                }
                else
                {
                    if (inputsLocked)
                    {
                        Fighters[i].ParseInputs(0);
                        continue;
                    }

                    ushort combinedInput = 0;
                    combinedInput |= playerInput[i * InputSize];
                    combinedInput |= (ushort)(playerInput[(i * InputSize) + 1] << 8);

                    if (i == 0) // Log for P1
                    {
                        GD.Print($"GameLoop P1: InputSize={InputSize} playerInputLen={playerInput.Length} Combined={combinedInput}");
                    }

                    Fighters[i].ParseInputs(combinedInput);
                }
            }

            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i] == null) continue;
                Nodes[i].PreTick();
            }

            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i] == null) continue;
                Nodes[i].Tick();
            }

            if (World == null)
            {
                MarkFatalRuntime(nameof(GameLoop), new[] { "World is null during simulation." });
                return;
            }
            World.Simulate();

            if (Fighters[0].Body.FixedPosition.X < Fighters[1].Body.FixedPosition.X)
            { Fighters[0].UpdateSide(true); Fighters[1].UpdateSide(false); }
            else if (Fighters[0].Body.FixedPosition.X > Fighters[1].Body.FixedPosition.X)
            { Fighters[0].UpdateSide(false); Fighters[1].UpdateSide(true); }

            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].Body.FixedPosition.X = Mathf.Clamp(
                    Fighters[i].Body.FixedPosition.X,
                    center - Global.MaxPlayersDistance / 2,
                    center + Global.MaxPlayersDistance / 2
                );
                Fighters[i].Body.UpdateColliders();
            }

            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i] == null) continue;
                Nodes[i].LateTick();
            }

            if (Frame % 60 == 0)
            {
                GD.Print($"Frame: {Frame}, Seed: {finalSeed}, InputsLocked: {inputsLocked}");
                if (Fighters[0] != null) GD.Print($"P1 Pos: {Fighters[0].Body.FixedPosition}, State: {Fighters[0].Animator.CurrentState}");
                if (Fighters[1] != null) GD.Print($"P2 Pos: {Fighters[1].Body.FixedPosition}, State: {Fighters[1].Animator.CurrentState}");
            }
        }

        // Generate inputs for your game
        //NOTICE: for every 8 inputs you need to change the index
        public byte[] ReadInputs(int id, int inputSize)
        {
            byte[] input = new byte[inputSize];
            string prefix = "";

            // If the GDScript addon is connected, prefer its inputs
            if (addonInputActive)
            {
                if (id >= 0 && id < addonRawInputs.Length)
                {
                    ushort raw = addonRawInputs[id];
                    input[0] = (byte)(raw & 0xFF);
                    if (inputSize > 1)
                        input[1] = (byte)((raw >> 8) & 0xFF);
                }
                return input;
            }

            switch (id)
            {
                case 0:
                    prefix = "k1";
                    break;
                case 1:
                    prefix = "k2";
                    break;
            }

            // Local helper: prefer InputMap actions but fall back to direct key checks
            bool ActionOrKey(string action, Key key)
            {
                if (InputMap.HasAction(action))
                    return Input.IsActionPressed(action);
                return Input.IsKeyPressed(key);
            }

            // Directional: try mapped actions first, else common keys
            if (ActionOrKey(prefix + "_up", Key.Up) && !ActionOrKey(prefix + "_down", Key.Down))
                input[0] |= Global.INPUT_UP;

            if (!ActionOrKey(prefix + "_up", Key.Up) && ActionOrKey(prefix + "_down", Key.Down))
                input[0] |= Global.INPUT_DOWN;

            if (ActionOrKey(prefix + "_left", Key.Left) && !ActionOrKey(prefix + "_right", Key.Right))
                input[0] |= Global.INPUT_LEFT;

            if (!ActionOrKey(prefix + "_left", Key.Left) && ActionOrKey(prefix + "_right", Key.Right))
                input[0] |= Global.INPUT_RIGHT;

            // Face buttons: fall back to Z/X/C/V for player 0 and Numpad/other for player 1 if needed
            if (ActionOrKey(prefix + "_face_a", Key.Z))
                input[0] |= Global.INPUT_FACE_A;

            if (ActionOrKey(prefix + "_face_b", Key.X))
                input[0] |= Global.INPUT_FACE_B;

            if (ActionOrKey(prefix + "_face_c", Key.C))
                input[0] |= Global.INPUT_FACE_C;

            if (ActionOrKey(prefix + "_face_d", Key.V))
                input[0] |= Global.INPUT_FACE_D;

            /*if (Input.IsActionPressed(prexif + "_macro_ab"))
                input |= Global.INPUT_FACE_A | Global.INPUT_FACE_B;

            if (Input.IsActionPressed(prexif + "_macro_ac"))
                input |= Global.INPUT_FACE_A | Global.INPUT_FACE_C;
            
            if (Input.IsActionPressed(prexif + "_macro_bc"))
                input |= Global.INPUT_FACE_B | Global.INPUT_FACE_C;

            if (Input.IsActionPressed(prexif + "_macro_abc"))
                input |= Global.INPUT_FACE_A | Global.INPUT_FACE_B | Global.INPUT_FACE_C;

            if (Input.IsActionPressed(prexif + "_macro_abcd"))
                input |= Global.INPUT_FACE_A | Global.INPUT_FACE_B | Global.INPUT_FACE_C | Global.INPUT_FACE_D;*/

            bool useTouch = TouchInput != null && TouchInput.IsVisibleInTree();
            if (id == 0 && useTouch)
            {
                ushort touchData = TouchInput.GetInput();
                input[0] |= (byte)(touchData & 0xFF);
                if (inputSize > 1)
                    input[1] |= (byte)((touchData >> 8) & 0xFF);
            }

            if (input[0] != 0)
            {
                GD.Print($"ReadInputs({id}): Detected Input! {input[0]}");
            }

            return input;
        }

        public void SaveState(BinaryWriter bw)
        {
            bw.Write(Frame);
            Monitor?.Serialize(bw);
            if (Nodes != null)
            {
                for (int i = 0; i < Nodes.Count; i++)
                    Nodes[i]?.Serialize(bw);
            }

            bw.Write(randomTest.X);
            bw.Write(randomTest.Y);
            bw.Write(randomTest.Z);
        }

        public void LoadState(BinaryReader br)
        {
            Frame = br.ReadInt32();
            Monitor?.Deserialize(br);
            if (Nodes != null)
            {
                for (int i = 0; i < Nodes.Count; i++)
                    Nodes[i]?.Deserialize(br);
            }

            randomTest.X = br.ReadInt32();
            randomTest.Y = br.ReadInt32();
            randomTest.Z = br.ReadInt32();
        }

        public byte[] GetLocalInput(int PlayerID, int InputSize)
        {
            byte[] result = ReadInputs(PlayerID, InputSize);

            return result;
        }

        private void InjectSharedAnimations(SakugaFighter fighter)
        {
            if (!enableSharedAnimationInjection)
            {
                // Animation injection disabled — managed externally via scene resources.
                GD.Print($"[InjectSharedAnimations] Skipped for {fighter.Name} (injection disabled).");
                return;
            }

            // Define mandatory state mappings (State Name -> Filename without extension)
            var aliases = new Dictionary<string, List<string>>
            {
                { "Bouncing Fight Idle", new List<string> { "Idle" } },
                { "Walking", new List<string> { "Walk_Forward" } },
                { "Walking Backward", new List<string> { "Walk_Back" } },
                { "Crouched Walking", new List<string> { "Crouch", "Crouch_Idle" } },
                { "Jump", new List<string> { "Jump_N", "Jump_F", "Jump_B" } },
                { "Jumping Down", new List<string> { "Landing" } },
                { "Stumble Backwards", new List<string> { "HitStun" } },
                { "Blocking", new List<string> { "BlockStun" } },
                { "Knocked Out (1)", new List<string> { "Knockdown" } }
            };

            string folderPath = "res://Fighters/Shared/Animations/";
            var sharedAnimations = LoadSharedAnimationLibrary(folderPath, fighter.Name);

            for (int playerIndex = 0; playerIndex < fighter.Animator.players.Length; playerIndex++)
            {
                var targetPlayer = fighter.Animator.players[playerIndex];
                var library = targetPlayer.GetAnimationLibrary("");
                if (library == null)
                {
                    GD.Print("[InjectSharedAnimations] Creating new AnimationLibrary...");
                    library = new AnimationLibrary();
                    targetPlayer.AddAnimationLibrary("", library);
                }

                AddSharedAnimationsToLibrary(library, sharedAnimations, aliases);
                EnsureDefaultAnimations(fighter, playerIndex, library);

                // Log per-player details for runtime verification
                try
                {
                    var apList = targetPlayer.GetAnimationList();
                    GD.Print($"[InjectSharedAnimations] Player {playerIndex} AnimationPlayer list for {fighter.Name}: {string.Join(", ", apList)}");
                }
                catch (System.Exception e)
                {
                    GD.PrintErr($"[InjectSharedAnimations] Failed to get AnimationPlayer list for {fighter.Name} player {playerIndex}: {e.Message}");
                }

                try
                {
                    var libList = library.GetAnimationList();
                    GD.Print($"[InjectSharedAnimations] Player {playerIndex} AnimationLibrary entries for {fighter.Name}: {string.Join(", ", libList)}");
                }
                catch (System.Exception e)
                {
                    GD.PrintErr($"[InjectSharedAnimations] Failed to get AnimationLibrary list for {fighter.Name} player {playerIndex}: {e.Message}");
                }
            }

            GD.Print($"[InjectSharedAnimations] Final Animation List for {fighter.Name}: {string.Join(", ", fighter.Animator.players[0].GetAnimationList())}");
        }

        private static Animation LoadAnimationFromResource(string path, string debugName)
        {
            var loadedRes = GD.Load(path);
            if (loadedRes == null)
            {
                GD.PrintErr($"[InjectSharedAnimations] Failed to load resource: {path}");
                return null;
            }

            Animation startAnim = null;

            if (loadedRes is PackedScene packedScene)
            {
                var tempNode = packedScene.Instantiate();
                var sourcePlayer = tempNode.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
                if (sourcePlayer == null)
                {
                    foreach (var child in tempNode.GetChildren())
                    {
                        if (child is AnimationPlayer ap) { sourcePlayer = ap; break; }
                    }
                }
                if (sourcePlayer != null)
                {
                    var animList = sourcePlayer.GetAnimationList();
                    if (animList.Length > 0)
                    {
                        startAnim = sourcePlayer.GetAnimation(animList[0]);
                    }
                }
                tempNode.QueueFree();
            }
            else if (loadedRes is AnimationLibrary animLib)
            {
                var list = animLib.GetAnimationList();
                if (list.Count > 0) startAnim = animLib.GetAnimation(list[0]);
            }
            else if (loadedRes is Animation a)
            {
                startAnim = a;
            }

            if (startAnim != null)
            {
                startAnim.LoopMode = Animation.LoopModeEnum.Linear;

                for (int i = 0; i < startAnim.GetTrackCount(); i++)
                {
                    string pathStr = startAnim.TrackGetPath(i).ToString();
                    if (pathStr.Contains("Skeleton:"))
                    {
                        int colonIndex = pathStr.IndexOf(':');
                        string boneName = pathStr[(colonIndex + 1)..];
                        startAnim.TrackSetPath(i, new NodePath($"GeneralSkeleton:{boneName}"));
                    }
                }
                return startAnim;
            }
            else
            {
                GD.PrintErr($"[InjectSharedAnimations] Could not find animation in {path} for {debugName}");
                return null;
            }
        }

        private Dictionary<string, Animation> LoadSharedAnimationLibrary(string folderPath, string fighterName)
        {
            var sharedAnimations = new Dictionary<string, Animation>();
            var dir = DirAccess.Open(folderPath);
            if (dir == null)
            {
                GD.PrintErr($"[InjectSharedAnimations] Failed to open directory: {folderPath}");
                return sharedAnimations;
            }

            dir.ListDirBegin();
            string fileName = dir.GetNext();
            while (fileName != "")
            {
                if (!dir.CurrentIsDir() && (fileName.EndsWith(".glb") || fileName.EndsWith(".gltf")))
                {
                    string fullPath = folderPath + fileName;
                    string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);

                    Animation anim = LoadAnimationFromResource(fullPath, fighterName);
                    if (anim != null && !sharedAnimations.ContainsKey(baseName))
                    {
                        sharedAnimations[baseName] = anim;
                    }
                }
                fileName = dir.GetNext();
            }

            return sharedAnimations;
        }

        private static void AddSharedAnimationsToLibrary(AnimationLibrary library, Dictionary<string, Animation> sharedAnimations, Dictionary<string, List<string>> aliases)
        {
            foreach (var kvp in sharedAnimations)
            {
                var animationCopy = (Animation)kvp.Value.Duplicate();
                if (!library.HasAnimation(kvp.Key))
                {
                    try
                    {
                        library.AddAnimation(kvp.Key, animationCopy);
                    }
                    catch (System.Exception e)
                    {
                        GD.PrintErr($"[InjectSharedAnimations] Could not add animation '{kvp.Key}' to library: {e.Message}");
                    }
                }

                if (aliases.TryGetValue(kvp.Key, out var aliasList))
                {
                    foreach (var alias in aliasList)
                    {
                        if (!library.HasAnimation(alias))
                        {
                            try
                            {
                                library.AddAnimation(alias, (Animation)animationCopy.Duplicate());
                            }
                            catch (System.Exception e)
                            {
                                GD.PrintErr($"[InjectSharedAnimations] Could not add alias '{alias}' for '{kvp.Key}': {e.Message}");
                            }
                        }
                    }
                }
            }
        }

        private void EnsureDefaultAnimations(SakugaFighter fighter, int playerIndex, AnimationLibrary library)
        {
            if (fighter.Animator?.States == null || fighter.Animator.States.Length == 0)
                return;

            string prefix = string.Empty;
            if (fighter.Animator.Prefixes != null && playerIndex < fighter.Animator.Prefixes.Length)
                prefix = fighter.Animator.Prefixes[playerIndex] ?? string.Empty;

            Animation fallback = GetFallbackAnimation(library, prefix);

            HashSet<string> requiredAnimations = new HashSet<string>();
            foreach (var state in fighter.Animator.States)
            {
                if (state?.animationSettings == null) continue;
                foreach (var animSettings in state.animationSettings)
                {
                    if (animSettings == null || string.IsNullOrEmpty(animSettings.SourceAnimation)) continue;
                    requiredAnimations.Add(prefix + animSettings.SourceAnimation);
                }
            }

            foreach (var animationName in requiredAnimations)
            {
                if (!library.HasAnimation(animationName))
                {
                    library.AddAnimation(animationName, (Animation)fallback.Duplicate());
                }
            }
        }

        private Animation GetFallbackAnimation(AnimationLibrary library, string prefix)
        {
            string[] candidates = new string[] { prefix + "Idle", "Idle" };
            foreach (var candidate in candidates)
            {
                if (library.HasAnimation(candidate))
                    return library.GetAnimation(candidate);
            }

            var available = library.GetAnimationList();
            if (available.Count > 0)
                return library.GetAnimation(available[0]);

            return CreatePlaceholderAnimation();
        }

        private static Animation CreatePlaceholderAnimation()
        {
            var placeholder = new Animation
            {
                Length = 0.1f,
                LoopMode = Animation.LoopModeEnum.Linear
            };
            return placeholder;
        }
    }
}
