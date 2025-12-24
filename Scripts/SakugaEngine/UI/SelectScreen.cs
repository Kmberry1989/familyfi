using Godot;
using SakugaEngine;
using SakugaEngine.Resources;
using System;
using System.Threading.Tasks;

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
        [Export] private OptionButton BotDifficultyOptions;

        //Hidden variables
        private bool P1Finished;
        private bool P2Finished;
        private bool isPlayer1SelectingStage = true;
        private bool AllSet = false;
        private bool hasP1Interacted = false;
        private CharSelectButton[] characterButtons;
        private StageSelectButton[] stageSelectionButtons;

        // CPU Selection Logic
        private bool isCpuSelecting = false;
        private double cpuSelectionTimer = 0.0f;
        private double cpuMoveTimer = 0.0f;
        private double cpuSelectionDuration = 2.0f;

        private const double CpuHoverInterval = 0.5f;

        private System.Random randomSelection;
        private Global.BotDifficulty selectedBotDifficulty = Global.BotDifficulty.MEDIUM;

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

            SetupBotDifficultyOptions();
            StartCpuSelection();
        }

        private void SetupBotDifficultyOptions()
        {
            if (BotDifficultyOptions == null) return;

            BotDifficultyOptions.Clear();
            foreach (Global.BotDifficulty difficulty in Enum.GetValues(typeof(Global.BotDifficulty)))
            {
                string label = difficulty.ToString().Replace("_", " ");
                BotDifficultyOptions.AddItem(label, (int)difficulty);
            }

            BotDifficultyOptions.Select((int)selectedBotDifficulty);
            BotDifficultyOptions.ItemSelected += OnBotDifficultyOptionSelected;
        }

        private void OnBotDifficultyOptionSelected(long index)
        {
            selectedBotDifficulty = (Global.BotDifficulty)index;
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
                hasP1Interacted = true;
                // Visual update happens in _PhysicsProcess currently
            }
            else
            {
                if (!hasP1Interacted)
                    hasP1Interacted = true;
                else
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
                            hasP1Interacted = true;
                        }
                        if (P1Down)
                        {
                            P1Selected += charactersContainer.Columns;
                            if (P1Selected >= fightersList.elements.Length)
                                P1Selected = fightersList.elements.Length - 1;
                            hasP1Interacted = true;
                        }
                        if (P1Left)
                        {
                            P1Selected--;
                            if (P1Selected < 0) P1Selected = 0;
                            hasP1Interacted = true;
                        }
                        if (P1Right)
                        {
                            P1Selected++;
                            if (P1Selected >= fightersList.elements.Length)
                                P1Selected = fightersList.elements.Length - 1;
                            hasP1Interacted = true;
                        }
                        if (P1Confirm)
                        {
                            if (!hasP1Interacted)
                                hasP1Interacted = true; // First "confirm" just interacts/selects if haven't yet
                            else
                                ConfirmPlayer1();
                        }
                    }

                    if (isCpuSelecting)
                    {
                        if (!P1Finished)
                        {
                            // Continue browsing while P1 is selecting
                            cpuMoveTimer += delta;

                            if (cpuMoveTimer > CpuHoverInterval)
                            {
                                P2Selected = GetCpuRandomSelection();
                                cpuMoveTimer = 0.0f;
                            }
                        }
                        else
                        {
                            // P1 Finished, lock in CPU
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
                            hasP1Interacted = false;
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
                        // Removed BGM Selection Up/Down
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
                        // Removed BGM Selection Up/Down
                        if (P2Confirm)
                        {
                            ConfirmStage();
                        }
                        if (P2Return)
                        {
                            P1Finished = false;
                            P2Finished = false;
                            hasP1Interacted = false;
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

            // Removed BGM UI Update
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
            Global.Match.botDifficulty = Global.BotDifficulty.ROOKIE; // Forces Rookie
            Global.Match.selectedMode = Global.SelectedMode.VERSUS;

            GD.Print($"MatchSetup Complete: P1={P1Selected}, P2={P2Selected}, Stage={StageSelected}, BGM={BGMSelected}");
        }

        private async Task FadeOutSelectionUI()
        {
            if (CharacterSelectMode != null || StageSelectMode != null)
            {
                Tween fadeTween = CreateTween();
                fadeTween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);

                if (CharacterSelectMode != null)
                {
                    Vector2 initialPosition = CharacterSelectMode.Position;
                    fadeTween.TweenProperty(CharacterSelectMode, "modulate:a", 0.0f, 0.35f);
                    fadeTween.Parallel().TweenProperty(CharacterSelectMode, "position", initialPosition + new Vector2(0, 50), 0.35f);
                }

                if (StageSelectMode != null)
                {
                    Vector2 initialPosition = StageSelectMode.Position;
                    fadeTween.TweenProperty(StageSelectMode, "modulate:a", 0.0f, 0.35f);
                    fadeTween.Parallel().TweenProperty(StageSelectMode, "position", initialPosition + new Vector2(0, 50), 0.35f);
                }

                await ToSignal(fadeTween, "finished");

                if (CharacterSelectMode != null)
                    CharacterSelectMode.Visible = false;

                if (StageSelectMode != null)
                    StageSelectMode.Visible = false;
            }
        }

        async void ShowBattleCard()
        {
            GD.Print("ShowBattleCard called");
            if (BattleCardLayer != null)
            {
                await FadeOutSelectionUI();

                GD.Print("BattleCardLayer found, displaying...");
                BattleCardLayer.ZIndex = 100; // Ensure it's on top

                // Smoothly hide the select UI before showing the card
                // Note: FadeOutSelectionUI already handled visibility and fade out.
                // The previous redundant selectTween block here was causing a hang because it awaited an empty tween.

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

                // Prepare starting transforms for animation
                Vector2 p1TargetPosition = P1ReadyRender.Position;
                Vector2 p2TargetPosition = P2ReadyRender.Position;
                Vector2 vsTargetPosition = VSLabel.Position;

                const float readyOffset = 520.0f;
                P1ReadyRender.Position = p1TargetPosition + new Vector2(-readyOffset, 0.0f);
                P2ReadyRender.Position = p2TargetPosition + new Vector2(readyOffset, 0.0f);
                P1ReadyRender.Modulate = new Color(1, 1, 1, 0);
                P2ReadyRender.Modulate = new Color(1, 1, 1, 0);

                VSLabel.Position = vsTargetPosition + new Vector2(0.0f, -40.0f);
                VSLabel.Scale = new Vector2(1.1f, 1.1f);
                VSLabel.Modulate = new Color(1, 1, 1, 0);

                Tween tween = CreateTween();
                tween.SetParallel(true);

                tween.TweenProperty(BattleCardLayer, "modulate:a", 1.0f, 0.55f)
                    .SetTrans(Tween.TransitionType.Cubic)
                    .SetEase(Tween.EaseType.Out);

                tween.TweenProperty(P1ReadyRender, "position", p1TargetPosition, 0.7f)
                    .SetTrans(Tween.TransitionType.Sine)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(P2ReadyRender, "position", p2TargetPosition, 0.7f)
                    .SetTrans(Tween.TransitionType.Sine)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(P1ReadyRender, "modulate:a", 1.0f, 0.45f)
                    .SetTrans(Tween.TransitionType.Cubic)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(P2ReadyRender, "modulate:a", 1.0f, 0.45f)
                    .SetTrans(Tween.TransitionType.Cubic)
                    .SetEase(Tween.EaseType.Out);

                tween.TweenProperty(VSLabel, "position", vsTargetPosition, 0.4f)
                    .SetTrans(Tween.TransitionType.Back)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(VSLabel, "scale", Vector2.One, 0.4f)
                    .SetTrans(Tween.TransitionType.Back)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(VSLabel, "modulate:a", 1.0f, 0.35f)
                    .SetTrans(Tween.TransitionType.Sine)
                    .SetEase(Tween.EaseType.Out);

                await ToSignal(tween, "finished");

                GD.Print("Battle Card displayed, waiting 1.75s...");
                await ToSignal(GetTree().CreateTimer(1.75f), "timeout");

                Tween fadeOutTween = CreateTween();
                fadeOutTween.TweenProperty(BattleCardLayer, "modulate:a", 0.0f, 0.5f)
                    .SetTrans(Tween.TransitionType.Cubic)
                    .SetEase(Tween.EaseType.In);
                Vector2 p1Target = P1ReadyRender.Position;
                Vector2 p2Target = P2ReadyRender.Position;
                Vector2 vsTarget = VSLabel.Position;

                float spreadDistance = 320.0f;
                P1ReadyRender.Position = p1Target - new Vector2(spreadDistance, 0);
                P2ReadyRender.Position = p2Target + new Vector2(spreadDistance, 0);
                VSLabel.Position = vsTarget + new Vector2(0, -80);

                VSLabel.Modulate = new Color(1, 1, 1, 0);
                VSLabel.Scale = new Vector2(1.2f, 1.2f);

                // Fade In and slide together
                tween = CreateTween();
                tween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);

                tween.TweenProperty(BattleCardLayer, "modulate:a", 1.0f, 0.6f);
                tween.Parallel().TweenProperty(P1ReadyRender, "position", p1Target, 0.7f).SetTrans(Tween.TransitionType.Quart).SetEase(Tween.EaseType.Out);
                tween.Parallel().TweenProperty(P2ReadyRender, "position", p2Target, 0.7f).SetTrans(Tween.TransitionType.Quart).SetEase(Tween.EaseType.Out);
                tween.Parallel().TweenProperty(VSLabel, "position", vsTarget, 0.5f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
                tween.Parallel().TweenProperty(VSLabel, "modulate:a", 1.0f, 0.5f);
                tween.Parallel().TweenProperty(VSLabel, "scale", new Vector2(1, 1), 0.5f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

                await ToSignal(tween, "finished");

                // Wait with the card visible
                GD.Print("Battle Card displayed, waiting 2.0s...");
                await ToSignal(GetTree().CreateTimer(2.0f), "timeout");

                fadeOutTween = CreateTween();
                fadeOutTween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
                fadeOutTween.TweenProperty(BattleCardLayer, "modulate:a", 0.0f, 0.6f);
                await ToSignal(fadeOutTween, "finished");
            }
            else
            {
                GD.PrintErr("BattleCardLayer is NULL! Skipping transition effect.");
            }

            GD.Print("Transitioning to FightScene...");
            GetTree().ChangeSceneToFile("res://Scenes/FightScene.tscn");
        }
    }
}
