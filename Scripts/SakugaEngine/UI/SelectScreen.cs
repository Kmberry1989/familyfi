using Godot;
using SakugaEngine;
using SakugaEngine.Resources;

namespace SakugaEngine.UI
{
    public partial class SelectScreen : Node
    {
        [ExportCategory("Settings")]
        [Export] private FighterList fightersList;
        [Export] private StageList stagesList;
        [Export] private BGMList songsList;
        [Export(PropertyHint.Enum, "Character_Select,Stage_Select")] private byte selectionMode;
        [Export] private Control CharacterSelectMode;
        [Export] private Control StageSelectMode;
        [Export] private int P1Selected = 0;
        [Export] private int P2Selected = 0;
        [Export] private int StageSelected = -2;
        [Export] private int BGMSelected = -2;

        [ExportCategory("Character Select")]

        [Export] private TextureRect P1SelectedRender;
        [Export] private TextureRect P2SelectedRender;
        [Export] private Label P1SelectedName;
        [Export] private Label P2SelectedName;
        [Export] private TextureRect P1Cursor;
        [Export] private TextureRect P2Cursor;
        [Export] private PackedScene charactersButtonElement;
        [Export] private GridContainer charactersContainer;
        [Export] private Texture2D randomCharPortrait;
        [Export] private Texture2D randomCharRender;

        [ExportCategory("Battle Card")]
        [Export] private Control BattleCardLayer;
        [Export] private TextureRect P1ReadyRender;
        [Export] private TextureRect P2ReadyRender;
        [Export] private Label VSLabel;

        [ExportCategory("Stage Select")]
        [Export] private TextureRect StageSelectedRender;
        [Export] private Label StageSelectedName;
        [Export] private Label SongSelectedName;
        [Export] private Control P1SelectingStage;
        [Export] private Control P2SelectingStage;
        [Export] private Control StageCursor;
        [Export] private PackedScene stagesButtonElement;
        [Export] private HBoxContainer stagesContainer;
        [Export] private Texture2D randomStageThumbnail;
        [Export] private Texture2D autoStageThumbnail;

        //Hidden variables
        private bool P1Finished;
        private bool P2Finished;
        private bool isPlayer1SelectingStage = true;
        private bool AllSet = false;
        private CharSelectButton[] characterButtons;
        private StageSelectButton[] stageSelectionButtons;

        // CPU Selection Logic
        private bool isCpuSelecting = false;
        private double cpuSelectionTimer = 0.0f;
        private double cpuMoveTimer = 0.0f;
        private double cpuSelectionDuration = 2.0f;

        private const double CpuHoverInterval = 0.25f;

        private System.Random randomSelection;

        public override void _Ready()
        {
            base._Ready();

            randomSelection = new System.Random();

            characterButtons = new CharSelectButton[fightersList.elements.Length];
            for (int i = 0; i < fightersList.elements.Length; i++)
            {
                CharSelectButton temp = charactersButtonElement.Instantiate() as CharSelectButton;
                temp.Name = fightersList.elements[i].Profile.ShortName + "_Portrait";
                temp.GetNode<TextureRect>("Portrait").Texture = fightersList.elements[i].Profile.Portrait;
                temp.Index = i;
                temp.OnPressed += OnCharacterButtonPressed;
                characterButtons[i] = temp;
                charactersContainer.AddChild(temp);
            }

            stageSelectionButtons = new StageSelectButton[stagesList.elements.Length + 2];
            for (int i = -2; i < stagesList.elements.Length; i++)
            {
                StageSelectButton temp = stagesButtonElement.Instantiate() as StageSelectButton;
                if (i == -2)//Auto
                {
                    temp.Name = "Auto_Portrait";
                    temp.Texture = autoStageThumbnail;
                }
                else if (i == -1)//Random
                {
                    temp.Name = "Random_Portrait";
                    temp.Texture = randomStageThumbnail;
                }
                else if (i >= 0)
                {
                    temp.Name = stagesList.elements[i].Name + "_Portrait";
                    temp.Texture = stagesList.elements[i].Thumbnail;
                }
                temp.Index = i;
                temp.OnPressed += OnStageButtonPressed;
                stageSelectionButtons[i + 2] = temp;
                stagesContainer.AddChild(temp);
            }

            StartCpuSelection();
        }

        private void StartCpuSelection()
        {
            if (characterButtons != null && P2Selected < characterButtons.Length)
            {
                characterButtons[P2Selected].IsSelected = false;
            }

            isCpuSelecting = true;
            P2Finished = false;
            cpuSelectionTimer = 0.0f;
            cpuMoveTimer = 0.0f;
            P2Selected = GetCpuRandomSelection();
        }

        private int GetCpuRandomSelection()
        {
            if (fightersList.elements.Length <= 1)
                return 0;

            int selection = randomSelection.Next(0, fightersList.elements.Length);
            while (selection == P1Selected)
            {
                selection = randomSelection.Next(0, fightersList.elements.Length);
            }

            return selection;
        }

        private void OnCharacterButtonPressed(CharSelectButton btn)
        {
            if (selectionMode != 0 || P1Finished) return; // Ignore if not in char select or already done

            if (P1Selected != btn.Index)
            {
                P1Selected = btn.Index;
                // Visual update happens in _PhysicsProcess currently
            }
            else
            {
                ConfirmPlayer1();
            }
        }

        private void ConfirmPlayer1()
        {
            characterButtons[P1Selected].IsSelected = true;
            P1Finished = true;
        }

        private void OnStageButtonPressed(StageSelectButton btn)
        {
            GD.Print($"OnStageButtonPressed. Index: {btn.Index}. Mode: {selectionMode}. AllSet: {AllSet}");
            if (selectionMode != 1 || AllSet) return; // Ignore if not in stage select or already done

            if (StageSelected != btn.Index)
            {
                GD.Print("Selecting new stage.");
                StageSelected = btn.Index;
            }
            else
            {
                GD.Print("Confirming selected stage.");
                ConfirmStage();
            }
        }

        private void ConfirmStage()
        {
            GD.Print($"ConfirmStage called. Current StageSelected: {StageSelected}");
            if (StageSelected == -2)
                StageSelected = fightersList.elements[P2Selected].Profile.AutoStage;
            else if (StageSelected == -1)
                StageSelected = randomSelection.Next(0, stagesList.elements.Length);

            if (BGMSelected == -2)
                BGMSelected = fightersList.elements[P1Selected].Profile.AutoBGM;
            else if (BGMSelected == -1)
                BGMSelected = randomSelection.Next(0, songsList.elements.Length);

            GD.Print("Stage confirmed. Proceeding to MatchSetup and ShowBattleCard.");
            AllSet = true;
            MatchSetup();
            ShowBattleCard();
        }

        public override void _PhysicsProcess(double delta)
        {
            if (AllSet) return;
            base._PhysicsProcess(delta);
            //Player 1 inputs
            bool P1Up = Input.IsActionJustPressed("k1_up");
            bool P1Down = Input.IsActionJustPressed("k1_down");
            bool P1Left = Input.IsActionJustPressed("k1_left");
            bool P1Right = Input.IsActionJustPressed("k1_right");
            bool P1Confirm = Input.IsActionJustPressed("k1_face_a");
            bool P1Return = Input.IsActionJustPressed("k1_face_b");
            //Player 2 inputs
            bool P2Up = Input.IsActionJustPressed("k2_up");
            bool P2Down = Input.IsActionJustPressed("k2_down");
            bool P2Left = Input.IsActionJustPressed("k2_left");
            bool P2Right = Input.IsActionJustPressed("k2_right");
            bool P2Confirm = Input.IsActionJustPressed("k2_face_a");
            bool P2Return = Input.IsActionJustPressed("k2_face_b");

            switch (selectionMode)
            {
                case 0:
                    // Update hover states
                    for (int i = 0; i < characterButtons.Length; i++)
                    {
                        characterButtons[i].IsHovered = (i == P1Selected || i == P2Selected);
                    }

                    //Player 1 character selection
                    if (!P1Finished)
                    {
                        if (P1Up)
                        {
                            P1Selected -= charactersContainer.Columns;
                            if (P1Selected < 0) P1Selected = 0;
                        }
                        if (P1Down)
                        {
                            P1Selected += charactersContainer.Columns;
                            if (P1Selected >= fightersList.elements.Length)
                                P1Selected = fightersList.elements.Length - 1;
                        }
                        if (P1Left)
                        {
                            P1Selected--;
                            if (P1Selected < 0) P1Selected = 0;
                        }
                        if (P1Right)
                        {
                            P1Selected++;
                            if (P1Selected >= fightersList.elements.Length)
                                P1Selected = fightersList.elements.Length - 1;
                        }
                        if (P1Confirm)
                        {
                            ConfirmPlayer1();
                        }
                    }

                    if (isCpuSelecting)
                    {
                        cpuSelectionTimer += delta;
                        cpuMoveTimer += delta;

                        if (cpuMoveTimer > CpuHoverInterval)
                        {
                            P2Selected = GetCpuRandomSelection();
                            cpuMoveTimer = 0.0f;
                        }

                        if (cpuSelectionTimer > cpuSelectionDuration)
                        {
                            isCpuSelecting = false;

                            // Final Selection
                            P2Selected = GetCpuRandomSelection();
                            characterButtons[P2Selected].IsSelected = true;
                            P2Finished = true;
                        }
                    }
                    else
                    {
                        if (P1Return)
                        {
                            characterButtons[P1Selected].IsSelected = false;
                            characterButtons[P2Selected].IsSelected = false;
                            P1Finished = false;
                            P2Finished = false; // Reset P2 as well
                            StartCpuSelection();
                        }
                    }
                    // P2 manual selection removed for CPU mode
                    break;
                case 1:
                    // Update Stage Hover
                    for (int i = 0; i < stageSelectionButtons.Length; i++)
                    {
                        // Array index matches StageSelected + 2 (-2 -> 0, -1 -> 1, 0 -> 2...)
                        stageSelectionButtons[i].IsHovered = (i == StageSelected + 2);
                    }

                    if (isPlayer1SelectingStage)
                    {
                        if (P1Left)
                        {
                            StageSelected--;
                            if (StageSelected < -2) StageSelected = -2;
                        }
                        if (P1Right)
                        {
                            StageSelected++;
                            if (StageSelected >= stagesList.elements.Length)
                                StageSelected = stagesList.elements.Length - 1;
                        }
                        if (P1Up)
                        {
                            BGMSelected--;
                            if (BGMSelected < -2) BGMSelected = -2;
                        }
                        if (P1Down)
                        {
                            BGMSelected++;
                            if (BGMSelected >= songsList.elements.Length)
                                BGMSelected = songsList.elements.Length - 1;
                        }
                        if (P1Confirm)
                        {
                            ConfirmStage();
                        }
                        if (P1Return)
                        {
                            P1Finished = false;
                            P2Finished = false;
                            characterButtons[P1Selected].IsSelected = false;
                            characterButtons[P2Selected].IsSelected = false;
                        }
                    }
                    else
                    {
                        if (P2Left)
                        {
                            StageSelected--;
                            if (StageSelected < -2) StageSelected = -2;
                        }
                        if (P2Right)
                        {
                            StageSelected++;
                            if (StageSelected >= stagesList.elements.Length)
                                StageSelected = stagesList.elements.Length - 1;
                        }
                        if (P2Up)
                        {
                            BGMSelected--;
                            if (BGMSelected < -2) BGMSelected = -2;
                        }
                        if (P2Down)
                        {
                            BGMSelected++;
                            if (BGMSelected >= songsList.elements.Length)
                                BGMSelected = songsList.elements.Length - 1;
                        }
                        if (P2Confirm)
                        {
                            ConfirmStage();
                        }
                        if (P2Return)
                        {
                            P1Finished = false;
                            P2Finished = false;
                            characterButtons[P1Selected].IsSelected = false;
                            characterButtons[P2Selected].IsSelected = false;
                        }
                    }
                    break;
            }

            selectionMode = P1Finished && P2Finished ? (byte)1 : (byte)0;
            CharacterSelectMode.Visible = selectionMode == 0;
            StageSelectMode.Visible = selectionMode == 1;

            P1Cursor.GlobalPosition = characterButtons[P1Selected].GlobalPosition;
            if (P2Selected < characterButtons.Length)
                P2Cursor.GlobalPosition = characterButtons[P2Selected].GlobalPosition;

            // Load READY sprites for display
            string p1Name = fightersList.elements[P1Selected].Profile.ShortName;
            string p1Path = $"res://Sprites/Icons/ReadySelect/{p1Name.ToUpper()}_READY.png";
            if (ResourceLoader.Exists(p1Path))
                P1SelectedRender.Texture = ResourceLoader.Load<Texture2D>(p1Path);
            else
                P1SelectedRender.Texture = fightersList.elements[P1Selected].Profile.Render; // Fallback

            P1SelectedName.Text = fightersList.elements[P1Selected].Profile.FighterName;

            string p2Name = fightersList.elements[P2Selected].Profile.ShortName;
            string p2Path = $"res://Sprites/Icons/ReadySelect/{p2Name.ToUpper()}_READY.png";
            if (ResourceLoader.Exists(p2Path))
            {
                P2SelectedRender.Texture = ResourceLoader.Load<Texture2D>(p2Path);
                P2SelectedRender.FlipH = true;
            }
            else
            {
                P2SelectedRender.Texture = fightersList.elements[P2Selected].Profile.Render; // Fallback
                P2SelectedRender.FlipH = true;
            }

            P2SelectedName.Text = fightersList.elements[P2Selected].Profile.FighterName;

            P1SelectingStage.Visible = isPlayer1SelectingStage;
            P2SelectingStage.Visible = !isPlayer1SelectingStage;

            StageCursor.GlobalPosition = stageSelectionButtons[StageSelected + 2].GlobalPosition;

            if (selectionMode == 1)
            {
                if (StageSelected == -2)//Auto
                {
                    StageSelectedRender.Texture = autoStageThumbnail;
                    StageSelectedName.Text = "Auto";
                }
                else if (StageSelected == -1)//Random
                {
                    StageSelectedRender.Texture = randomStageThumbnail;
                    StageSelectedName.Text = "Random";
                }
                else if (StageSelected >= 0)
                {
                    StageSelectedRender.Texture = stagesList.elements[StageSelected].Thumbnail;
                    StageSelectedName.Text = stagesList.elements[StageSelected].Name;
                }
            }
            else
            {
                StageSelectedRender.Texture = null;
            }

            if (BGMSelected == -2)//Auto
                SongSelectedName.Text = "Auto";
            else if (BGMSelected == -1)//Random
                SongSelectedName.Text = "Random";
            else if (BGMSelected >= 0)
                SongSelectedName.Text = songsList.elements[BGMSelected].SongName;
        }

        void MatchSetup()
        {
            Global.Match.Player1 = new MatchPlayerSettings()
            {
                selectedCharacter = P1Selected,
                selectedColor = 0,
                selectedDevice = 0
            };

            Global.Match.Player2 = new MatchPlayerSettings()
            {
                selectedCharacter = P2Selected,
                selectedColor = 0,
                selectedDevice = -1
            };

            Global.Match.selectedStage = StageSelected;
            Global.Match.selectedBGM = BGMSelected;
            Global.Match.roundsToWin = 2;
            Global.Match.roundTime = 99;
            Global.Match.botDifficulty = Global.BotDifficulty.MEDIUM;
            Global.Match.selectedMode = Global.SelectedMode.VERSUS;

            GD.Print($"MatchSetup Complete: P1={P1Selected}, P2={P2Selected}, Stage={StageSelected}, BGM={BGMSelected}");
        }

        async void ShowBattleCard()
        {
            GD.Print("ShowBattleCard called");
            if (BattleCardLayer != null)
            {
                GD.Print("BattleCardLayer found, displaying...");
                BattleCardLayer.ZIndex = 100; // Ensure it's on top
                BattleCardLayer.Visible = true;
                BattleCardLayer.Modulate = new Color(1, 1, 1, 0);

                // Load Ready Sprites
                string p1Name = fightersList.elements[P1Selected].Profile.ShortName;
                string p2Name = fightersList.elements[P2Selected].Profile.ShortName;

                string p1Path = $"res://Sprites/Icons/ReadySelect/{p1Name.ToUpper()}_READY.png";
                string p2Path = $"res://Sprites/Icons/ReadySelect/{p2Name.ToUpper()}_READY.png";

                if (ResourceLoader.Exists(p1Path))
                    P1ReadyRender.Texture = ResourceLoader.Load<Texture2D>(p1Path);

                if (ResourceLoader.Exists(p2Path))
                {
                    P2ReadyRender.Texture = ResourceLoader.Load<Texture2D>(p2Path);
                    P2ReadyRender.FlipH = true; // KEEP THIS FLIP for VS Screen
                }
                else
                {
                    P2ReadyRender.Texture = fightersList.elements[P2Selected].Profile.Render;
                    P2ReadyRender.FlipH = true;
                }

                // Fade In
                Tween tween = CreateTween();
                tween.TweenProperty(BattleCardLayer, "modulate:a", 1.0f, 0.5f);
                await ToSignal(tween, "finished");

                // Wait
                GD.Print("Battle Card displayed, waiting 2.0s...");
                await ToSignal(GetTree().CreateTimer(2.0f), "timeout");
            }
            else
            {
                GD.PrintErr("BattleCardLayer is NULL! Skipping transition effect.");
            }

            GD.Print("Transitioning to TestScene...");
            GetTree().ChangeSceneToFile("res://Scenes/TestScene.tscn");
        }
    }
}
