using Godot;
using System.IO;
using SakugaEngine.Resources;

namespace SakugaEngine
{
    [GlobalClass]
    public partial class InputManager : Node
    {
        public InputRegistry[] InputHistory = new InputRegistry[Global.InputHistorySize];
        public int CurrentHistory = 0;
        public int InputSide;

        public bool CheckMotionInputs(MotionInputs motion)
        {
            if (motion == null) return false;
            if (motion.ValidInputs == null) return false;

            // Iterate over all valid input patterns for this move
            for (int i = 0; i < motion.ValidInputs.Length; i++)
            {
                // We start searching for the LAST input in the sequence at the current frame
                int currentHistoryIdx = CurrentHistory;
                bool patternFound = true;

                // Iterate backwards through the required inputs of the pattern (last to first)
                for (int j = motion.ValidInputs[i].Inputs.Length - 1; j >= 0; j--)
                {
                    InputSequence inputRequirement = motion.ValidInputs[i].Inputs[j];

                    // Search backwards from currentHistoryIdx until we find a match or run out of buffer
                    int foundIndex = -1;

                    // Limit search to the input buffer duration
                    int searchLimit = motion.InputBuffer > 0 ? motion.InputBuffer : int.MaxValue;

                    // Track how many history frames we've walked while we scan backwards. We cap by both buffer
                    // duration and history size to avoid infinite loops in the circular buffer.
                    int framesSearched = 0;
                    int historySteps = 0;
                    int checkIdx = currentHistoryIdx;

                    while (historySteps < Global.InputHistorySize && framesSearched <= searchLimit)
                    {
                        // Check if the input at checkIdx (and its duration) matches the requirement
                        // For the very last input (j == length-1), it MUST be active NOW (at CurrentHistory) usually, 
                        // or at least very recently. 
                        // However, for "fuzzy" feel, we might allow the last input to be a few frames old if we want 'kara' style or buffer forgiveness.
                        // But usually, CheckMoves is called because we want to execute NOW.
                        // Let's assume strict timing for the *last* input (must be active or JustPressed), 
                        // but standard "fuzzy" scanning for previous inputs.

                        // BUT: The existing code used `CurrentHistory - ...` implying strict alignment.
                        // We want: Find Input[Last] within tolerances. Then Find Input[Last-1] before that, etc.

                        bool match = CheckSingleInput(checkIdx, inputRequirement, motion.AbsoluteDirection, motion.DirectionalChargeLimit);

                        if (match)
                        {
                            foundIndex = checkIdx;
                            break;
                        }

                        // Move back one history entry and accumulate the time that entry lasted.
                        framesSearched += InputHistory[checkIdx].duration;
                        historySteps++;

                        checkIdx--;
                        if (checkIdx < 0) checkIdx += Global.InputHistorySize;
                    }

                    if (foundIndex != -1)
                    {
                        // Found this step!
                        // The next step (previous in sequence) must be found BEFORE this one in time.
                        // So we set currentHistoryIdx to foundIndex - 1 (or -duration?)
                        // If we want to ensure we don't reuse the same frame for multiple inputs unless allowed:
                        // usually we move the pointer back.
                        currentHistoryIdx = foundIndex - 1;
                        if (currentHistoryIdx < 0) currentHistoryIdx += Global.InputHistorySize;
                    }
                    else
                    {
                        // Could not find this required input within the time window
                        patternFound = false;
                        break;
                    }
                }

                if (patternFound)
                {
                    GD.Print($"CheckMotionInputs: Fuzzy Pattern {i} Matched!");
                    return true;
                }
            }

            return false;
        }

        private bool CheckSingleInput(int historyIdx, InputSequence requirement, bool absDir, int chargeLimit)
        {
            Global.DirectionalInputs d = requirement.Directional;
            Global.ButtonInputs b = requirement.Buttons;
            Global.ButtonMode dMode = requirement.DirectionalMode;
            Global.ButtonMode bMode = requirement.ButtonMode;

            bool matchesDir = false;
            bool matchesBtn = false;

            // Check Direction
            if (d == 0)
            {
                // If requirement is Neutral (0), we check if internal inputs are neutral? 
                // Or does 0 mean "ignoring direction"?
                // In your Global.cs, you have `CheckDirectionalInputs` handle 0 as Neutral Check if Mode is NOT_PRESSED?
                // Let's use your existing logic helpers.
                matchesDir = CheckDirectionalInputs(historyIdx, d, dMode, absDir);
            }
            else
            {
                matchesDir = CheckDirectionalInputs(historyIdx, d, dMode, absDir);
            }

            // Check Buttons
            if (b == 0)
            {
                // If 0, usually means "No button required" -> matches any button state? 
                // OR "Must accept empty button"?
                // The original code:
                /*
                    if (directionals > 0 && buttons == 0) 
                        validInput = CheckDirectionalInputs...
                    else if (directionals == 0 && buttons > 0)
                        validInput = CheckButtonInputs...
                    else 
                        validInput = CheckDirectionalInputs && CheckButtonInputs
                 */
                // This implies "AND" logic if both are present.
                matchesBtn = true; // Assume true if not checking buttons
            }
            else
            {
                matchesBtn = CheckButtonInputs(historyIdx, b, bMode);
            }

            bool hasDir = d != 0;
            bool hasBtn = b != 0;
            bool chargeMatch = chargeLimit > 0 && hasDir && CheckChargeInputs(historyIdx, d, chargeLimit);

            // If we have BOTH, we need BOTH to match.
            // If we have only Directions, we ignore buttons? 
            // The original logic:
            if (hasDir && !hasBtn) return matchesDir || chargeMatch;
            if (!hasDir && hasBtn) return matchesBtn;
            if (hasDir && hasBtn) return (matchesDir || chargeMatch) && matchesBtn;
            return matchesDir && matchesBtn;
        }

        public bool CheckInputEnd(MotionInputs motion)
        {
            var lastInputs = motion.ValidInputs[0].Inputs;
            var inputToCheck = lastInputs[^1];
            Global.DirectionalInputs directionals = inputToCheck.Directional;
            Global.ButtonInputs buttons = inputToCheck.Buttons;

            bool validInput;

            if (directionals > 0 && buttons == 0)
                validInput = !CheckDirectionalInputs(CurrentHistory, directionals, Global.ButtonMode.HOLD, motion.AbsoluteDirection);
            else if (directionals == 0 && buttons > 0)
                validInput = !CheckButtonInputs(CurrentHistory, buttons, Global.ButtonMode.HOLD);
            else
                validInput = !CheckDirectionalInputs(CurrentHistory, directionals, Global.ButtonMode.HOLD, motion.AbsoluteDirection) &&
                !CheckButtonInputs(CurrentHistory, buttons, Global.ButtonMode.HOLD);

            return validInput;
        }



        public bool CheckDirectionalInputs(int index, Global.DirectionalInputs buttonNumber, Global.ButtonMode buttonMode, bool absDirection)
        {
            bool left = false;
            bool right = false;
            bool up = false;
            bool down = false;

            int _left = Global.INPUT_LEFT;
            if (InputSide < 0) _left = Global.INPUT_RIGHT;

            int _right = Global.INPUT_RIGHT;
            if (InputSide < 0) _right = Global.INPUT_LEFT;

            switch (buttonMode)
            {
                case Global.ButtonMode.PRESS:
                    left = WasPressed(index, _left);
                    right = WasPressed(index, _right);
                    up = WasPressed(index, Global.INPUT_UP);
                    down = WasPressed(index, Global.INPUT_DOWN);
                    break;
                case Global.ButtonMode.HOLD:
                    left = IsBeingPressed(index, _left);
                    right = IsBeingPressed(index, _right);
                    up = IsBeingPressed(index, Global.INPUT_UP);
                    down = IsBeingPressed(index, Global.INPUT_DOWN);
                    break;
                case Global.ButtonMode.RELEASE:
                    left = WasReleased(index, _left);
                    right = WasReleased(index, _right);
                    up = WasReleased(index, Global.INPUT_UP);
                    down = WasReleased(index, Global.INPUT_DOWN);
                    break;
                case Global.ButtonMode.WAS_PRESSED:
                    left = WasBeingPressed(index, _left);
                    right = WasBeingPressed(index, _right);
                    up = WasBeingPressed(index, Global.INPUT_UP);
                    down = WasBeingPressed(index, Global.INPUT_DOWN);
                    break;
                case Global.ButtonMode.NOT_PRESSED:
                    left = !IsBeingPressed(index, _left);
                    right = !IsBeingPressed(index, _right);
                    up = !IsBeingPressed(index, Global.INPUT_UP);
                    down = !IsBeingPressed(index, Global.INPUT_DOWN);
                    break;
            }

            bool absV = !absDirection || (!up && !down);
            bool absH = !absDirection || (!left && !right);

            bool notP = buttonMode == Global.ButtonMode.NOT_PRESSED;
            bool canAbsH = notP || absH;
            bool canAbsV = notP || absV;
            bool neutralDirection = notP ? (down && up && left && right) : (!down && !up && !left && !right);

            return (buttonNumber == Global.DirectionalInputs.DOWN && down && canAbsH) ||
                (buttonNumber == Global.DirectionalInputs.LEFT && left && canAbsV) ||
                (buttonNumber == Global.DirectionalInputs.RIGHT && right && canAbsV) ||
                (buttonNumber == Global.DirectionalInputs.UP && up && canAbsH) ||
                (buttonNumber == Global.DirectionalInputs.DOWN_LEFT && down && left) ||
                (buttonNumber == Global.DirectionalInputs.DOWN_RIGHT && down && right) ||
                (buttonNumber == Global.DirectionalInputs.UP_LEFT && up && left) ||
                (buttonNumber == Global.DirectionalInputs.UP_RIGHT && up && right) ||
                (buttonNumber == 0 && neutralDirection);
        }

        public bool CheckButtonInputs(int index, Global.ButtonInputs buttonNumber, Global.ButtonMode buttonMode)
        {
            bool action_ba = false;
            bool action_bb = false;
            bool action_bc = false;
            bool action_bd = false;

            switch (buttonMode)
            {
                case Global.ButtonMode.PRESS:
                    action_ba = WasPressed(index, Global.INPUT_FACE_A);
                    action_bb = WasPressed(index, Global.INPUT_FACE_B);
                    action_bc = WasPressed(index, Global.INPUT_FACE_C);
                    action_bd = WasPressed(index, Global.INPUT_FACE_D);
                    break;
                case Global.ButtonMode.HOLD:
                    action_ba = IsBeingPressed(index, Global.INPUT_FACE_A);
                    action_bb = IsBeingPressed(index, Global.INPUT_FACE_B);
                    action_bc = IsBeingPressed(index, Global.INPUT_FACE_C);
                    action_bd = IsBeingPressed(index, Global.INPUT_FACE_D);
                    break;
                case Global.ButtonMode.RELEASE:
                    action_ba = WasReleased(index, Global.INPUT_FACE_A);
                    action_bb = WasReleased(index, Global.INPUT_FACE_B);
                    action_bc = WasReleased(index, Global.INPUT_FACE_C);
                    action_bd = WasReleased(index, Global.INPUT_FACE_D);
                    break;
                case Global.ButtonMode.WAS_PRESSED:
                    action_ba = WasBeingPressed(index, Global.INPUT_FACE_A);
                    action_bb = WasBeingPressed(index, Global.INPUT_FACE_B);
                    action_bc = WasBeingPressed(index, Global.INPUT_FACE_C);
                    action_bd = WasBeingPressed(index, Global.INPUT_FACE_D);
                    break;
                case Global.ButtonMode.NOT_PRESSED:
                    action_ba = !IsBeingPressed(index, Global.INPUT_FACE_A);
                    action_bb = !IsBeingPressed(index, Global.INPUT_FACE_B);
                    action_bc = !IsBeingPressed(index, Global.INPUT_FACE_C);
                    action_bd = !IsBeingPressed(index, Global.INPUT_FACE_D);
                    break;
            }

            bool canA = (buttonNumber & Global.ButtonInputs.FACE_A) > 0;
            bool canB = (buttonNumber & Global.ButtonInputs.FACE_B) > 0;
            bool canC = (buttonNumber & Global.ButtonInputs.FACE_C) > 0;
            bool canD = (buttonNumber & Global.ButtonInputs.FACE_D) > 0;

            return (buttonNumber == 0 && !action_ba && !action_bb && !action_bc && !action_bd) ||
                (!canA || canA == action_ba) && (!canB || canB == action_bb) &&
                (!canC || canC == action_bc) && (!canD || canD == action_bd);
        }

        public bool CheckChargeInputs(int index, Global.DirectionalInputs buttonNumber, int dirCharge)
        {
            if (dirCharge == 0) return false;

            bool _left = IsBeingPressed(index, Global.INPUT_LEFT);
            bool _right = IsBeingPressed(index, Global.INPUT_RIGHT);

            bool left = InputSide < 0 ? _right : _left;
            // Removed unused 'right' variable

            bool up = IsBeingPressed(index, Global.INPUT_UP);
            bool down = IsBeingPressed(index, Global.INPUT_DOWN);

            bool checkInputs = (buttonNumber == Global.DirectionalInputs.LEFT && left && Mathf.Abs(InputHistory[index].hCharge) >= dirCharge) ||
                (buttonNumber == Global.DirectionalInputs.DOWN && down && Mathf.Abs(InputHistory[index].vCharge) >= dirCharge) ||
                (buttonNumber == Global.DirectionalInputs.DOWN_LEFT && left && Mathf.Abs(InputHistory[index].hCharge) >= dirCharge && down && InputHistory[index].vCharge <= -dirCharge) ||
                (buttonNumber == Global.DirectionalInputs.UP_LEFT && left && Mathf.Abs(InputHistory[index].hCharge) >= dirCharge && up && InputHistory[index].vCharge >= dirCharge);

            return checkInputs;
        }

        public void InsertToHistory(ushort input)
        {
            if (InputHistory[CurrentHistory].rawInput != input)
            {
                CurrentHistory++;
                if (CurrentHistory >= Global.InputHistorySize) CurrentHistory = 0;

                InputHistory[CurrentHistory].rawInput = input;
                InputHistory[CurrentHistory].duration = 0;

                //Get the charge values from the previous input
                int previousInput = CurrentHistory - 1;
                if (previousInput < 0) previousInput += Global.InputHistorySize;

                InputHistory[CurrentHistory].hCharge = InputHistory[previousInput].hCharge;
                InputHistory[CurrentHistory].vCharge = InputHistory[previousInput].vCharge;
                InputHistory[CurrentHistory].bCharge = InputHistory[previousInput].bCharge;
            }

            InputHistory[CurrentHistory].duration++;
            ChargeBuffer();
        }

        public bool IsBeingPressed(int index, int input)
        {
            return (InputHistory[index].rawInput & input) != 0;
        }
        public bool WasBeingPressed(int index, int input)
        {
            int previousInput = index - 1;
            if (previousInput < 0) previousInput += Global.InputHistorySize;

            return (InputHistory[index].rawInput & input) == 0 &&
                (InputHistory[previousInput % Global.InputHistorySize].rawInput & input) != 0;
        }
        public bool WasPressed(int index, int input)
        {
            int previousInput = index - 1;
            if (previousInput < 0) previousInput += Global.InputHistorySize;

            return (InputHistory[index].rawInput & input) != 0 &&
                (InputHistory[previousInput % Global.InputHistorySize].rawInput & input) == 0 &&
                InputHistory[index].duration == 1;
        }
        public bool WasReleased(int index, int input)
        {
            int previousInput = index - 1;
            if (previousInput < 0) previousInput += Global.InputHistorySize;

            return (InputHistory[index].rawInput & input) == 0 &&
                (InputHistory[previousInput % Global.InputHistorySize].rawInput & input) != 0 &&
                InputHistory[index].duration == 1;
        }

        public bool IsDifferentInputs(int index)
        {
            int previousInput = index - 1;
            if (previousInput < 0) previousInput += Global.InputHistorySize;

            return InputHistory[index].rawInput != InputHistory[previousInput % Global.InputHistorySize].rawInput;
        }

        public bool InputChanged(int index, int input, bool changedThisFrame = true)
        {
            int previousInput = index - 1;
            if (previousInput < 0) previousInput += Global.InputHistorySize;

            bool currentInputCkech = (InputHistory[index].rawInput & input) != 0;
            bool previousInputCkech = (InputHistory[previousInput % Global.InputHistorySize].rawInput & input) != 0;
            bool isRecent = changedThisFrame && InputHistory[index].duration <= 1;
            return currentInputCkech == previousInputCkech && isRecent;
        }

        public bool FaceButtonsChanged(int index)
        {
            int previousInput = index - 1;
            if (previousInput < 0) previousInput += Global.InputHistorySize;

            bool CurA = (InputHistory[index].rawInput & Global.INPUT_FACE_A) != 0;
            bool CurB = (InputHistory[index].rawInput & Global.INPUT_FACE_B) != 0;
            bool CurC = (InputHistory[index].rawInput & Global.INPUT_FACE_C) != 0;
            bool CurD = (InputHistory[index].rawInput & Global.INPUT_FACE_D) != 0;

            bool PrevA = (InputHistory[previousInput % Global.InputHistorySize].rawInput & Global.INPUT_FACE_A) != 0;
            bool PrevB = (InputHistory[previousInput % Global.InputHistorySize].rawInput & Global.INPUT_FACE_B) != 0;
            bool PrevC = (InputHistory[previousInput % Global.InputHistorySize].rawInput & Global.INPUT_FACE_C) != 0;
            bool PrevD = (InputHistory[previousInput % Global.InputHistorySize].rawInput & Global.INPUT_FACE_D) != 0;

            bool isThisFrame = InputHistory[index].duration <= 1;

            return (CurA != PrevA || CurB != PrevB || CurC != PrevC || CurD != PrevD) && isThisFrame;
        }

        private void ChargeBuffer()
        {
            if (IsBeingPressed(CurrentHistory, Global.INPUT_LEFT))
            {
                if (InputHistory[CurrentHistory].hCharge > 0) InputHistory[CurrentHistory].hCharge = 0;
                InputHistory[CurrentHistory].hCharge--;
            }
            else if (IsBeingPressed(CurrentHistory, Global.INPUT_RIGHT))
            {
                if (InputHistory[CurrentHistory].hCharge < 0) InputHistory[CurrentHistory].hCharge = 0;
                InputHistory[CurrentHistory].hCharge++;
            }
            else
            {
                if (InputHistory[CurrentHistory].hCharge != 0) InputHistory[CurrentHistory].hCharge = 0;
            }

            if (IsBeingPressed(CurrentHistory, Global.INPUT_UP))
            {
                if (InputHistory[CurrentHistory].vCharge < 0) InputHistory[CurrentHistory].vCharge = 0;
                InputHistory[CurrentHistory].vCharge++;
            }
            else if (IsBeingPressed(CurrentHistory, Global.INPUT_DOWN))
            {
                if (InputHistory[CurrentHistory].vCharge > 0) InputHistory[CurrentHistory].vCharge = 0;
                InputHistory[CurrentHistory].vCharge--;
            }
            else
            {
                if (InputHistory[CurrentHistory].vCharge != 0) InputHistory[CurrentHistory].vCharge = 0;
            }

            if (IsBeingPressed(CurrentHistory, Global.INPUT_ANY_BUTTON))
            {
                InputHistory[CurrentHistory].bCharge++;
            }
            else
            {
                InputHistory[CurrentHistory].bCharge = 0;
            }
        }

        public InputRegistry CurrentInput() => InputHistory[CurrentHistory];
        public ushort InputBufferDuration() => CurrentInput().bCharge;
        public bool IsNeutral() => CurrentInput().IsNull;

        public void Serialize(BinaryWriter bw)
        {
            for (int i = 0; i < Global.InputHistorySize; i++)
                InputHistory[i].Serialize(bw);

            bw.Write(CurrentHistory);
            bw.Write(InputSide);
        }

        public void Deserialize(BinaryReader br)
        {
            for (int i = 0; i < Global.InputHistorySize; i++)
                InputHistory[i].Deserialize(br);

            CurrentHistory = br.ReadInt32();
            InputSide = br.ReadInt32();
        }


    }

    [System.Serializable]
    public struct InputRegistry
    {
        public ushort rawInput;
        public ushort duration;
        public short hCharge;
        public short vCharge;
        public ushort bCharge;

        public readonly bool IsNull => rawInput == 0;

        public readonly void Serialize(BinaryWriter bw)
        {
            bw.Write(rawInput);
            bw.Write(duration);
            bw.Write(hCharge);
            bw.Write(vCharge);
            bw.Write(bCharge);
        }

        public void Deserialize(BinaryReader br)
        {
            rawInput = br.ReadUInt16();
            duration = br.ReadUInt16();
            hCharge = br.ReadInt16();
            vCharge = br.ReadInt16();
            bCharge = br.ReadUInt16();
        }

        public readonly override string ToString()
        {
            return $"({rawInput}, {duration})";
        }

    };
}
