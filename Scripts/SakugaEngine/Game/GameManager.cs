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
            bool touchInputAvailable = TouchInput != null && TouchInput.IsVisibleInTree() && DisplayServer.IsTouchscreenAvailable();
            GD.Print($"Setup: P1 Device={Global.Match.Player1.selectedDevice}, P2 Device={Global.Match.Player2.selectedDevice}. Touch Controls Available: {touchInputAvailable}.");

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
            int expectedInputSize = (int)(InputSize * Fighters.Length);
            if (playerInput == null || playerInput.Length < expectedInputSize)
            {
                GD.PrintErr($"GameLoop: playerInput length ({playerInput?.Length ?? 0}) is smaller than expected ({expectedInputSize}).");
                return;
            }

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
            string prefix = "";

            switch (id)
            {
                case 0:
                    prefix = "k1";
                    break;
                case 1:
                    prefix = "k2";
                    break;
            }

            if (Input.IsActionPressed(prefix + "_up") && !Input.IsActionPressed(prefix + "_down"))
                input[0] |= Global.INPUT_UP;

            if (!Input.IsActionPressed(prefix + "_up") && Input.IsActionPressed(prefix + "_down"))
                input[0] |= Global.INPUT_DOWN;

            if (Input.IsActionPressed(prefix + "_left") && !Input.IsActionPressed(prefix + "_right"))
                input[0] |= Global.INPUT_LEFT;

            if (!Input.IsActionPressed(prefix + "_left") && Input.IsActionPressed(prefix + "_right"))
                input[0] |= Global.INPUT_RIGHT;

            if (Input.IsActionPressed(prefix + "_face_a"))
                input[0] |= Global.INPUT_FACE_A;

            if (Input.IsActionPressed(prefix + "_face_b"))
                input[0] |= Global.INPUT_FACE_B;

            if (Input.IsActionPressed(prefix + "_face_c"))
                input[0] |= Global.INPUT_FACE_C;

            if (Input.IsActionPressed(prefix + "_face_d"))
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

            bool useTouch = DisplayServer.IsTouchscreenAvailable();
            if (id == 0 && TouchInput != null && TouchInput.IsVisibleInTree() && useTouch)
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

            if (fighter.Animator == null || fighter.Animator.players == null || fighter.Animator.players.Length == 0)
            {
                GD.PrintErr("[InjectSharedAnimations] No Animator or Players found!");
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
                EnsureDefaultAnimations(fighter, playerIndex, targetPlayer, library);
            }

            GD.Print($"[InjectSharedAnimations] Final Animation List for {fighter.Name}: {string.Join(", ", fighter.Animator.players[0].GetAnimationList())}");
        }

        private Animation LoadAnimationFromResource(string path, string debugName)
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
                        int colonIndex = pathStr.IndexOf(":");
                        string boneName = pathStr.Substring(colonIndex + 1);
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

        private void AddSharedAnimationsToLibrary(AnimationLibrary library, Dictionary<string, Animation> sharedAnimations, Dictionary<string, List<string>> aliases)
        {
            foreach (var kvp in sharedAnimations)
            {
                var animationCopy = (Animation)kvp.Value.Duplicate();
                if (!library.HasAnimation(kvp.Key))
                {
                    library.AddAnimation(kvp.Key, animationCopy);
                }

                if (aliases.TryGetValue(kvp.Key, out var aliasList))
                {
                    foreach (var alias in aliasList)
                    {
                        if (!library.HasAnimation(alias))
                        {
                            library.AddAnimation(alias, (Animation)animationCopy.Duplicate());
                        }
                    }
                }
            }
        }

        private void EnsureDefaultAnimations(SakugaFighter fighter, int playerIndex, AnimationPlayer player, AnimationLibrary library)
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

        private Animation CreatePlaceholderAnimation()
        {
            var placeholder = new Animation();
            placeholder.Length = 0.1f;
            placeholder.LoopMode = Animation.LoopModeEnum.Linear;
            return placeholder;
        }
    }
}
