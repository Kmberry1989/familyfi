using Godot;
using System.IO;
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
        public uint InputSize;

        private List<SakugaNode> Nodes;
        private SakugaFighter[] Fighters;
        private PhysicsWorld World;

        private HealthHUD healthHUD;
        private MetersHUD metersHUD;

        private int Frame = 0;
        private int generatedSeed = 0;
        private int finalSeed = 0;

        Vector3I randomTest = new();

        public override void _Ready()
        {
            healthHUD = (HealthHUD)FighterUI.GetNode("GameHUD_Background");
            metersHUD = (MetersHUD)FighterUI.GetNode("GameHUD_Foreground");
            Nodes = new();
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            if (Fighters == null) return;
            if (Monitor == null) return;

            if (!BGMSource.Playing) BGMSource.Play();
            SeedViewer.Text = finalSeed.ToString();

            if (Input.IsActionJustPressed("toggle_hitboxes"))
                Global.ShowHitboxes = !Global.ShowHitboxes;
        }

        public void SetBGM()
        {
            if (BGMSource != null && !BGMSource.Playing && songsList.elements[selectedBGM].clip != null)
                BGMSource.Stream = songsList.elements[selectedBGM].clip;
        }

        public void Render()
        {
            RenderNodes();
            Camera.UpdateCamera(Fighters[0], Fighters[1]);
            healthHUD.UpdateHealthBars(Fighters, Monitor);
            metersHUD.UpdateMeters(Fighters);
        }

        void RenderNodes()
        {
            foreach (SakugaNode node in Nodes)
            {
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
            int posX = Fighters[0].Body.FixedPosition.X + Fighters[1].Body.FixedPosition.X;
            int posY = Fighters[0].Body.FixedPosition.Y + Fighters[1].Body.FixedPosition.Y;
            int stateFrame = Fighters[0].Animator.Frame + Fighters[0].Animator.CurrentState + Fighters[1].Animator.Frame + Fighters[1].Animator.CurrentState;
            return generatedSeed + posX + posY + stateFrame + (Frame * Global.SimulationScale) + Monitor.Clock;
        }

        public void Setup()
        {
            if (GetChildren().Count > 0)
            {
                foreach (Node child in GetChildren())
                {
                    child.QueueFree();
                }
            }

            Frame = 0;

            CreateStage(selectedStage);

            World = new PhysicsWorld();
            if (Nodes == null) Nodes = new();
            Nodes.Clear();
            Fighters = new SakugaFighter[2];

            CreateFighter(player1Character, 0);
            CreateFighter(player2Character, 1);

            Fighters[0].SetOpponent(Fighters[1]);
            Fighters[1].SetOpponent(Fighters[0]);

            //AI test (select it in a better way later)
            Fighters[0].UseAI = Global.Match.Player1.selectedDevice == -1;
            Fighters[1].UseAI = Global.Match.Player2.selectedDevice == -1;
            GD.Print($"Setup: P1 Device={Global.Match.Player1.selectedDevice}, P2 Device={Global.Match.Player2.selectedDevice}. P1 Forces Touch: True (Debug).");

            GenerateBaseSeed();

            if (Monitor != null) Monitor.Initialize(Fighters);
            else GD.PrintErr("GameManager: Monitor is null!");

            if (healthHUD == null && FighterUI != null) healthHUD = (HealthHUD)FighterUI.GetNode("GameHUD_Background");
            if (healthHUD != null) healthHUD.Setup(Fighters);
            else GD.PrintErr("GameManager: healthHUD is null! (Check FighterUI connection)");

            if (metersHUD == null && FighterUI != null) metersHUD = (MetersHUD)FighterUI.GetNode("GameHUD_Foreground");
            if (metersHUD != null) metersHUD.Setup(Fighters);
            else GD.PrintErr("GameManager: metersHUD is null! (Check FighterUI connection)");
        }

        public void CreateFighter(int characterIndex, int playerIndex)
        {
            if (characterIndex < 0 || characterIndex >= fightersList.elements.Length)
            {
                GD.PrintErr($"Invalid Character Index: {characterIndex}! Defaulting to 0.");
                characterIndex = 0;
            }
            Node temp = fightersList.elements[characterIndex].Instance.Instantiate();
            Fighters[playerIndex] = temp as SakugaFighter;
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
            AddChild(temp);
        }

        public void AddActor(SakugaNode newNode, bool isPhysicsBody = true)
        {
            AddChild(newNode);
            Nodes.Add(newNode);
            if (isPhysicsBody) World.AddBody((newNode as SakugaActor).Body);
        }

        public void GameLoop(byte[] playerInput)
        {
            finalSeed = CalculateSeed();
            Global.UpdateRNG(finalSeed);
            Frame++;
            Monitor.Tick();

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
                    if (Fighters[i].Brain != null)
                    {
                        Fighters[i].Brain.SelectCommand();
                        Fighters[i].Brain.UpdateCommand();
                    }
                    // Else: Brain is missing, do nothing (dummy behavior)
                }
                else
                {
                    ushort combinedInput = 0;
                    combinedInput |= playerInput[i * InputSize];
                    combinedInput |= (ushort)(playerInput[(i * InputSize) + 1] << 8);
                    Fighters[i].ParseInputs(combinedInput);
                }
            }

            for (int i = 0; i < Nodes.Count; i++)
                Nodes[i].PreTick();

            for (int i = 0; i < Nodes.Count; i++)
                Nodes[i].Tick();

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
                Nodes[i].LateTick();

            if (Frame % 60 == 0)
            {
                GD.Print($"Frame: {Frame}, Seed: {finalSeed}");
                if (Fighters[0] != null) GD.Print($"P1 Pos: {Fighters[0].Body.FixedPosition}, State: {Fighters[0].Animator.CurrentState}");
                if (Fighters[1] != null) GD.Print($"P2 Pos: {Fighters[1].Body.FixedPosition}, State: {Fighters[1].Animator.CurrentState}");
            }
        }

        // Generate inputs for your game
        //NOTICE: for every 8 inputs you need to change the index
        public byte[] ReadInputs(int id, int inputSize)
        {
            byte[] input = new byte[inputSize];
            string prexif = "";

            switch (id)
            {
                case 0:
                    prexif = "k1";
                    break;
                case 1:
                    prexif = "k2";
                    break;
            }

            if (Input.IsActionPressed(prexif + "_up") && !Input.IsActionPressed(prexif + "_down"))
                input[0] |= Global.INPUT_UP;

            if (!Input.IsActionPressed(prexif + "_up") && Input.IsActionPressed(prexif + "_down"))
                input[0] |= Global.INPUT_DOWN;

            if (Input.IsActionPressed(prexif + "_left") && !Input.IsActionPressed(prexif + "_right"))
                input[0] |= Global.INPUT_LEFT;

            if (!Input.IsActionPressed(prexif + "_left") && Input.IsActionPressed(prexif + "_right"))
                input[0] |= Global.INPUT_RIGHT;

            if (Input.IsActionPressed(prexif + "_face_a"))
                input[0] |= Global.INPUT_FACE_A;

            if (Input.IsActionPressed(prexif + "_face_b"))
                input[0] |= Global.INPUT_FACE_B;

            if (Input.IsActionPressed(prexif + "_face_c"))
                input[0] |= Global.INPUT_FACE_C;

            if (Input.IsActionPressed(prexif + "_face_d"))
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

            bool useTouch = false;
            // Original logic: P1 uses touch if device is 0?
            // If device is -1 (AI), this is skipped.
            // If device is 0 (P1), we check if they selected Touch.
            // But we don't have device selection.
            // Let's allow Touch ALWAYS for P1/P2 if the MobileControls are present.
            // This is safer.
            if (TouchInput != null && TouchInput.IsVisibleInTree()) useTouch = true;


            if (TouchInput != null && TouchInput.IsVisibleInTree() && useTouch)
            {
                ushort touchData = TouchInput.GetInput();
                input[0] |= (byte)(touchData & 0xFF);
                if (inputSize > 1)
                    input[1] |= (byte)((touchData >> 8) & 0xFF);
            }

            return input;
        }

        public void SaveState(BinaryWriter bw)
        {
            bw.Write(Frame);
            Monitor.Serialize(bw);
            for (int i = 0; i < Nodes.Count; i++)
                Nodes[i].Serialize(bw);

            bw.Write(randomTest.X);
            bw.Write(randomTest.Y);
            bw.Write(randomTest.Z);
        }

        public void LoadState(BinaryReader br)
        {
            Frame = br.ReadInt32();
            Monitor.Deserialize(br);
            for (int i = 0; i < Nodes.Count; i++)
                Nodes[i].Deserialize(br);

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
            GD.Print($"[InjectSharedAnimations] Starting for {fighter.Name}...");
            var animMap = new Dictionary<string, string>
            {
                {"Idle", "res://Fighters/Shared/Animations/Bouncing Fight Idle.glb"},
                {"Walk_Forward", "res://Fighters/Shared/Animations/Walking.glb"},
                {"Walk_Back", "res://Fighters/Shared/Animations/Walking Backward.glb"},
                {"Crouch_Idle", "res://Fighters/Shared/Animations/Crouched Walking.glb"}
            };

            if (fighter.Animator == null || fighter.Animator.players == null || fighter.Animator.players.Length == 0)
            {
                GD.PrintErr("[InjectSharedAnimations] No Animator or Players found!");
                return;
            }

            var targetPlayer = fighter.Animator.players[0];
            var library = targetPlayer.GetAnimationLibrary("");
            if (library == null)
            {
                GD.Print("[InjectSharedAnimations] Creating new AnimationLibrary...");
                library = new AnimationLibrary();
                targetPlayer.AddAnimationLibrary("", library);
            }

            foreach (var kvp in animMap)
            {
                var loadedRes = GD.Load(kvp.Value);
                if (loadedRes == null)
                {
                    GD.PrintErr($"[InjectSharedAnimations] Failed to load resource: {kvp.Value}");
                    continue;
                }

                Animation startAnim = null;
                Node tempNode = null;
                AnimationPlayer sourcePlayer = null;

                if (loadedRes is PackedScene packedScene)
                {
                    tempNode = packedScene.Instantiate();
                    sourcePlayer = tempNode.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
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
                        if (animList.Length > 0) startAnim = sourcePlayer.GetAnimation(animList[0]);
                    }
                }
                else if (loadedRes is AnimationLibrary animLib)
                {
                    var animList = animLib.GetAnimationList();
                    if (animList.Count > 0) startAnim = animLib.GetAnimation(animList[0]);
                }

                if (startAnim != null)
                {
                    var animDup = (Animation)startAnim.Duplicate();
                    animDup.LoopMode = Animation.LoopModeEnum.Linear;

                    if (sourcePlayer != null)
                    {
                        var sourceRoot = sourcePlayer.RootNode.ToString();
                        var targetRoot = targetPlayer.RootNode.ToString();
                        if (!string.IsNullOrEmpty(sourceRoot) && !string.IsNullOrEmpty(targetRoot) && sourceRoot != targetRoot)
                        {
                            for (int track = 0; track < animDup.GetTrackCount(); track++)
                            {
                                var trackPath = animDup.TrackGetPath(track).ToString();
                                if (trackPath.StartsWith(sourceRoot))
                                {
                                    var retargeted = trackPath.Replace(sourceRoot, targetRoot);
                                    animDup.TrackSetPath(track, new NodePath(retargeted));
                                }
                            }
                        }
                    }

                    if (library.HasAnimation(kvp.Key)) library.RemoveAnimation(kvp.Key);
                    library.AddAnimation(kvp.Key, animDup);
                    GD.Print($"[InjectSharedAnimations] Injected '{kvp.Key}' successfully.");
                }
                else
                {
                    GD.PrintErr($"[InjectSharedAnimations] Could not find animation in {kvp.Value}");
                }

                if (tempNode != null) tempNode.Free();
            }
            GD.Print($"[InjectSharedAnimations] Final Animation List for {fighter.Name}: {string.Join(", ", targetPlayer.GetAnimationList())}");
        }
    }
}
