using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Sandbox.Game.Entities.Blocks;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;
using IMyBatteryBlock = Sandbox.ModAPI.Ingame.IMyBatteryBlock;
using IMyCryoChamber = Sandbox.ModAPI.Ingame.IMyCryoChamber;
using IMyDoor = Sandbox.ModAPI.Ingame.IMyDoor;
using IMyFunctionalBlock = Sandbox.ModAPI.Ingame.IMyFunctionalBlock;
using IMyProgrammableBlock = Sandbox.ModAPI.Ingame.IMyProgrammableBlock;
using IMySensorBlock = Sandbox.ModAPI.Ingame.IMySensorBlock;
using IMyShipConnector = Sandbox.ModAPI.Ingame.IMyShipConnector;
using IMyTerminalBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using IMyTextSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
using IMyThrust = Sandbox.ModAPI.Ingame.IMyThrust;


namespace IngameScript {
    class Program : MyGridProgram {
        // Go to:
        // https://github.com/malware-dev/MDK-SE/wiki/Quick-Introduction-to-Space-Engineers-Ingame-Scripts
        //
        // to learn more about ingame scripts.
        //
        //Blocks used in the script
        public List<IMyTextSurface> LCD = new List<IMyTextSurface>(); //Status output screens
        public List<IMyLandingGear> LG = new List<IMyLandingGear>(); //Landing gears
        public List<IMyShipConnector> TConn = new List<IMyShipConnector>(); //Top Connector
        public List<IMyShipConnector> BConn = new List<IMyShipConnector>(); //Bottom Connector
        public List<IMyProgrammableBlock> CCPB = new List<IMyProgrammableBlock>(); //Cruise control Programming Block
        public List<IMySensorBlock> TSens; //Top facing sensor
        public List<IMySensorBlock> BSens; //Bottom facing sensor
        public List<IMySensorBlock> PSens; //Cabin Sensor (People)
        public List<IMyDoor> TDoor = new List<IMyDoor>(); //Top Doors
        public List<IMyDoor> BDoor = new List<IMyDoor>(); //Bottom Doors
        public List<IMyBatteryBlock> Batt = new List<IMyBatteryBlock>(); //Batteries
        public List<IMyThrust> Thr = new List<IMyThrust>(); //Thrusters

        public static List<IMyTerminalBlock> TerminalBlockList = new List<IMyTerminalBlock>(); //declare an empty list of TerminalBlocks for later use in searches.
        public static List<IMyTerminalBlock> TerminalBlockListCurrentGrid = new List<IMyTerminalBlock>(); // "T:"

        //utility vars
        public bool RescanBlocksSuccess; //Script indicator that all essential blocks are found.
        public Dictionary<string, string> SettingsDictionary = new Dictionary<string, string>() {{"Output LCD", "T:Status LCD"}, {"Top Floor Connector", "T:Top Connector"}, {"Bottom Floor Connector", "T:Bottom Connector"}, {"Cruise Control PB", "T:Cruise Control"}, {"Top Sensor", "T:Top Sensor"}, {"Bottom Sensor", "T:Bottom Sensor"}, {"Passengers Sensor", "T:People Sensor"}, {"Top Floor Doors", "Top Floor"}, {"Bottom Floor Doors", "Bottom Floor"}, {"Landing Gear", "T:Landing Gear"}};

        public LogEngine Log;

        public string CurState;
        public string LastState;
        private string LiftIntent;

        public string[] States = {
            "Unknown",
            "Idle",
            "PrepDep", //Prepare for Departure

            "CrzDwn", //Cruise downwards
            "AppDwn", //Approach bottom floor
            "PrkDwn", //Parking at bottom floor

            "CrzUp", //Cruise upwards
            "AppUp", //Approach top floor
            "PrkUp", //Parking at top floor
        };

        //task scheduler
        public int Iteration; //Counts script iterations for unfrequent task schedling and task load distribution.
        public int IterativeMultiplier = 1; //Compensates for script execution speed

        public List<IMyTerminalBlock> GetAnyBlocksByPattern(string Pattern) { //Get AutoLCD2 type pattern, get back requested blocks, from current grids or otherwise.
            if (Pattern == null) {
                return TerminalBlockList; //return all on empty patern
            }

            List<IMyTerminalBlock> ReturnList = new List<IMyTerminalBlock>();
            if (Pattern.StartsWith("T:")) { //Return current grid Blocks only, by name.
                Pattern = Pattern.Substring(2); //Update pattern with T: removed.
                foreach (IMyTerminalBlock Block in TerminalBlockListCurrentGrid) {
                    if (Block.CustomName.Contains(Pattern) || Pattern == "*")
                        ReturnList.Add(Block);
                }

                return ReturnList;
            }

            if (Pattern.StartsWith("G:")) { //Return all group Blocks
                GridTerminalSystem.GetBlockGroupWithName(Pattern.Substring(2)).GetBlocks(ReturnList);
                return ReturnList;
            }

            foreach (IMyTerminalBlock Block in TerminalBlockList) {
                if (Block.CustomName.Contains(Pattern) || Pattern == "*")
                    ReturnList.Add(Block);
            }

            return ReturnList;
        }

        //parse Custom Data
        public static Dictionary<string, string> ParseCustomData(IMyTerminalBlock Block, Dictionary<string, string> Settings) { //Get current CustomData and parse values requested by the dictionary.
            var CustomData = new Dictionary<string, string>(); //Original Data
            var CustomDataSettings = new Dictionary<string, string>(); //Parsed Data
            string[] CustomDataLines = Block.CustomData.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < CustomDataLines.Length; i++) { //scan and prepare Custom Data for matching
                var line = CustomDataLines[i];
                string value;
                var pos = line.IndexOf('=');
                if (pos > -1) {
                    value = line.Substring(pos + 1).Trim();
                    line = line.Substring(0, pos).Trim();
                } else {
                    value = "";
                }

                CustomData.Add(line, value); //Save the setting
            }

            foreach (var Setting in Settings) {
                if (CustomData.ContainsKey(Setting.Key)) {
                    CustomDataSettings.Add(Setting.Key, CustomData[Setting.Key]);
                } else {
                    CustomData.Add(Setting.Key, Setting.Value);
                    Block.CustomData += "\n" + Setting.Key + " = " + Setting.Value;
                }
            }

            return CustomDataSettings;
        }

        public bool FilterThis(IMyTerminalBlock block) { return block.CubeGrid == Me.CubeGrid; }

        void RescanBlocks() {
            SettingsDictionary = ParseCustomData(Me, SettingsDictionary);

            TerminalBlockList = new List<IMyTerminalBlock>(); //reset Block lists
            TerminalBlockListCurrentGrid = new List<IMyTerminalBlock>();

            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(TerminalBlockList); //Acquire all "Smart" blocks
            //Echo("Total terminal blocks in the grid: " + TerminalBlockList.Count + "\n");
            foreach (IMyTerminalBlock Block in TerminalBlockList) {
                if (FilterThis(Block))
                    TerminalBlockListCurrentGrid.Add(Block); //Get Blocks of current Grid.
            }

            RescanBlocksSuccess = true; //preset the assumption that blocks are found, then deny if missing.
            try {
                //Find specific Blocks
                TConn = GetSpecificTypeBlocksByPattern<IMyShipConnector>("Top Floor Connector");
                BConn = GetSpecificTypeBlocksByPattern<IMyShipConnector>("Bottom Floor Connector");
                CCPB = GetSpecificTypeBlocksByPattern<IMyProgrammableBlock>("Cruise Control PB");
                TSens = GetSpecificTypeBlocksByPattern<IMySensorBlock>("Top Sensor");
                BSens = GetSpecificTypeBlocksByPattern<IMySensorBlock>("Bottom Sensor");
                PSens = GetSpecificTypeBlocksByPattern<IMySensorBlock>("Passengers Sensor");
                LCD = GetTextSurfaces("Output LCD"); //Output screens
                TDoor = GetSpecificTypeBlocksByPattern<IMyDoor>("Top Floor Doors");
                BDoor = GetSpecificTypeBlocksByPattern<IMyDoor>("Bottom Floor Doors");
                LG = GetSpecificTypeBlocksByPattern<IMyLandingGear>("Landing Gear");
            } catch (Exception e) {
                Log.Add(e.Message + "\n(in Blocks lookup)", Log.Error);
                RescanBlocksSuccess = false;
            }

            if (RescanBlocksSuccess) {
                Log.Add("Blocks rescanned success.");
            } else {
                Log.Add("Blocks rescan failed!");
            }

            Batt = GetSpecificTypeBlocksByPattern<IMyBatteryBlock>("*");
            Thr = GetSpecificTypeBlocksByPattern<IMyThrust>("*");
        }

        private List<T> GetSpecificTypeBlocksByPattern<T>(string dicIndex) where T : IMyTerminalBlock {
            //Log.Add("Pattern search started/nlooking for" + dicIndex + "/nof type" + typeof(T));
            List<T> Temp = new List<T>();
            try {
                if (SettingsDictionary[dicIndex].Length > 0) {
                    Temp = GetAnyBlocksByPattern(SettingsDictionary[dicIndex]).Where(block => block is T).Cast<T>().ToList();
                    //List<T> Temp = GetAnyBlocksByPattern(SettingsDictionary[dicIndex]).Where(block => block.ToString() == typeof(T).ToString()).Cast<T>().ToList();
                    //List<T> Temp = GetAnyBlocksByPattern(SettingsDictionary[dicIndex]).Where(block => typeof(T).ToString().Split('.').Last().Contains(block.GetType().ToString().Split('.').Last())).Cast<T>().ToList();
                    if (Temp.Count <= 0) {
                        Log.Add(dicIndex + " not found!", Log.Error);
                        RescanBlocksSuccess = false;
                    }
                }
            } catch (Exception e) {
                Temp = GetAnyBlocksByPattern(dicIndex).Where(block => block is T).Cast<T>().ToList();
            }

            return Temp;
        }

        public List<IMyTextSurface> GetTextSurfaces(string pattern) {
            List<IMyTerminalBlock> OutputBlocks = GetAnyBlocksByPattern(SettingsDictionary[pattern]);
            List<IMyTextSurface> OutputList = new List<IMyTextSurface>();
            foreach (IMyTerminalBlock block in OutputBlocks) {
                if (block == null) {
                    continue;
                }

                var provider = block as IMyTextSurfaceProvider;
                if (provider != null) { //is a valid screen provider
                    if (provider is IMyTextSurfaceProvider) { //is a valid screen (cryochamber is a provider for some reason, has no screens)
                        try {
                            OutputList.Add(provider.GetSurface(0));
                        } catch (Exception e) {
                            Log.Add(e.ToString(), Log.Error);
                        }
                    }


                    continue;
                }

                if (block.GetType() == typeof(IMyTextSurface)) {
                    OutputList.Add((IMyTextSurface) block);
                }
            }

            return OutputList;

            //OG code for getting screens
//                if (!(Block is IMyCockpit cockpit) && !(Block is IMyTextSurface surface))
//                    continue;
//
//
//            for (int i = 0; i < TerminalBlockListCurrentGrid.Count; i++) {
//                if (TerminalBlockListCurrentGrid[i].CustomName.Contains(LCD_Name)) {
//                    if (TerminalBlockListCurrentGrid[i].BlockDefinition.ToString().Contains("ProgrammableBlock")) {
//                        IMyProgrammableBlock block = (IMyProgrammableBlock) TerminalBlockListCurrentGrid[i];
//                        outputLcdList.Add(block.GetSurface(0));
//                    } else
//                        outputLcdList.Add((IMyTextSurface) TerminalBlockListCurrentGrid[i]);
//
//                    if (TerminalBlockListCurrentGrid[i].CustomName.Contains(my_Cockpit_name))
//                        my_Cockpit = (IMyCockpit) TerminalBlockListCurrentGrid[i];
//                }
//            }
        }

        public void EnableBlockList<T>(List<T> BlockList) where T: IMyFunctionalBlock {
            foreach (var block in BlockList) block.Enabled = true;
        }

        public void EnableBlockList<T>(List<T> BlockList, bool State) where T : IMyFunctionalBlock {
            foreach (var block in BlockList) block.Enabled = State;
        }

        public void Save() {
            // Called when the program needs to save its state. Use
            // this method to save your state to the Storage field
            // or some other means. 
            // 
            // This method is optional and can be removed if not
            // needed.
        }

        private void ConfigBlocks() {//enable all relevant blocks
            try {
                EnableBlockList(Batt);
                EnableBlockList(Thr);
                EnableBlockList(LG);
                EnableBlockList(TConn);
                EnableBlockList(BConn);
                EnableBlockList(CCPB);
                EnableBlockList(TSens);
                EnableBlockList(BSens);
                EnableBlockList(PSens);
                EnableBlockList(TDoor);
                EnableBlockList(BDoor);
                Log.Add("Blocks enabled.");
            } catch (Exception e) {
                Log.Add(e.Message, Log.Error);
                Iteration--;
            }
        }

        public void Main(string argument, UpdateType updateSource) {
            // The main entry point of the script, invoked every time
            // one of the programmable block's Run actions are invoked,
            // or the script updates itself. The updateSource argument
            // describes where the update came from. Be aware that the
            // updateSource is a  bitfield  and might contain more than 
            // one update type.
            // 
            // The method itself is required, but the arguments above
            // can be removed if not needed.

            if (argument.Length > 0) {
                Log.Add("Argument: " + argument);
                //CurState = argument;
            } else {
                Log.Add("No Argument.");
            }

            switch (updateSource) {
                case UpdateType.None:
                    break;
                case UpdateType.Terminal: //Script call trough terminal window
                //break;
                case UpdateType.Trigger: //Script call
//                    if (argument.ToLower().Contains("set100")) {
//                        Runtime.UpdateFrequency = UpdateFrequency.Update100;
//                        break;
//                    } else if (argument.ToLower().Contains("set10")) {
//                        Runtime.UpdateFrequency = UpdateFrequency.Update10;
//                        break;
//                    } else if (argument.ToLower().Contains("set1")) {
//                        Runtime.UpdateFrequency = UpdateFrequency.Update1;
//                        break;
//                    }
                    switch (argument.ToLower()) {
                        case "goup":
                            LiftIntent = "up";
                            CurState = "PrepDep";
                            break;
                        case "go_down":
                        case "godown":
                        case "godwn":
                        case "godn":
                            LiftIntent = "down";
                            CurState = "PrepDep";
                            break;
                    }

                    break;
                case UpdateType.Antenna:
                    break;
                case UpdateType.Mod:
                    break;
                case UpdateType.Script:
                    break;
                case UpdateType.Update1:
                    IterativeMultiplier = 100;
                    goto Update;
                case UpdateType.Update10:
                    IterativeMultiplier = 10;
                    goto Update;
                case UpdateType.Update100:
                    IterativeMultiplier = 1;
                    Update: //"goto" landing

                    switch (Iteration) {
                        case 0: //rescan blocks
                            RescanBlocks();
                            Iteration = RescanBlocksSuccess ? Iteration : 3;
                            break;
                        case 1:
                            ConfigBlocks();
                            break;
                        default:
                            Log.Add("NOP.");
                            break;
                    }

                    break;
                case UpdateType.Once:
                    break;
                case UpdateType.IGC:
                    break;
            }

            Log.Add("Current state: " + CurState);
            Log.Add("Current intent: " + LiftIntent
            );
            Log.Add("Det: " + PSens.First().CustomName + " - " + PSens.First().IsActive);

            //Main state machine
            switch (CurState) {
                case "Unknown":
                    Runtime.UpdateFrequency = UpdateFrequency.Update100;
                    CurState = "CrzUp";
                    if (LastState != "CrzUp") {
                        //CC, etc
                        EnableBlockList(Thr);
                    }

                    foreach (var gear in LG) {
                        if (gear.LockMode != LandingGearMode.Unlocked) {
                            CurState = "Unknown";
                        }
                    }

                    foreach (var Con in TConn) {
                        if (Con.Status != MyShipConnectorStatus.Unconnected) {
                            CurState = "PrkUp";
                            LiftIntent = "up";
                            break;
                        }
                    }

                    foreach (var Con in BConn) {
                        if (Con.Status != MyShipConnectorStatus.Unconnected) {
                            //CurState = "PrkDwn";
                            CurState = "Idle";
                            LiftIntent = "down";
                            break;
                        }
                    }

                    break;
                case "Idle":
                    Runtime.UpdateFrequency = UpdateFrequency.Update100;
                    if (LastState != CurState) {
                        EnableBlockList(Batt);
                        EnableBlockList(BDoor);
                        EnableBlockList(TDoor);
                        EnableBlockList(PSens);
                    }
                    EnableBlockList(Batt);
                    foreach (var gear in LG)
                        gear.Lock();
                    foreach (var Conn in TConn)
                        Conn.Connect();
                    foreach (var Conn in BConn)
                        Conn.Connect();
                    if (PSens.First().IsActive) {
                        if (LiftIntent == "up") {
                            foreach (var door in TDoor)
                                if (door.Status != DoorStatus.Open)
                                    door.OpenDoor();
                        } else if (LiftIntent == "down") {
                            foreach (var door in BDoor)
                                if (door.Status != DoorStatus.Open)
                                    door.OpenDoor();
                        }
                    } else {
                        foreach (var door in TDoor)
                            if (door.Status != DoorStatus.Closed)
                                door.CloseDoor();
                        foreach (var door in BDoor)
                            if (door.Status != DoorStatus.Closed)
                                door.CloseDoor();
                    }

                    break;

                case "PrepDep": //Prepare for Departure
                    bool doorsClosed = true;
                    foreach (var batt in Batt) {
                        if (LastState != CurState)
                            batt.Enabled = true;
                        batt.ChargeMode = ChargeMode.Auto;
                    }

                    foreach (var thr in Thr) {
                        if (LastState != CurState)
                            thr.Enabled = true;
                    }

                    foreach (var door in TDoor) {
                        if (LastState != CurState)
                            door.Enabled = true;
                        door.CloseDoor();
                        doorsClosed &= (door.Status == DoorStatus.Closed);
                    }

                    foreach (var door in BDoor) {
                        if (LastState != CurState)
                            door.Enabled = true;
                        door.CloseDoor();
                        doorsClosed &= (door.Status == DoorStatus.Closed);
                    }

                    if (doorsClosed) {
                        CurState = (LiftIntent == "up") ? "CrzUp" : "CrzDwn";
                    }

                    break;

                case "CrzDwn": //Cruise downwards
                    if (LastState != CurState) {
                        Runtime.UpdateFrequency = UpdateFrequency.Update1;

                        EnableBlockList(Thr);
                        EnableBlockList(Batt);
                        foreach (var batteryBlock in Batt) {
                            batteryBlock.ChargeMode = ChargeMode.Auto;
                        }

                        //TODO engines, CC, Update , etc
                    }

                    break;
                case "AppDwn": //Approach bottom floor
                case "PrkDwn": //Parking at bottom floor
                    Runtime.UpdateFrequency = UpdateFrequency.Update10;
                    //TODO CC 2~3
                    bool LG_Ready = false;
                    bool Con_Ready = false;
                    foreach (var gear in LG) {
                        if (gear.LockMode == LandingGearMode.ReadyToLock) {
                            LG_Ready = true;
                            break;
                        }
                    }

                    foreach (var Con in BConn) {
                        if (Con.Status == MyShipConnectorStatus.Connectable) {
                            //CurState = "PrkDwn";
                            Con_Ready = true;
                            break;
                        }
                    }

                    if (LG_Ready && Con_Ready) {
                        foreach (var gear in LG)
                            gear.Lock();
                        foreach (var Con in BConn)
                            Con.Connect();
                        //TODO turn engines off, turn off CC, batteries to recharge.
                        CurState = "Idle";
                    }

                    break;

                case "CrzUp": //Cruise upwards
                    break;
                case "AppUp": //Approach top floor
                case "PrkUp": //Parking at top floor
                    break;
                default:
                    Log.Add("Unknown state: " + CurState, Log.Error);
                    break;
            }

            LastState = CurState;
            //Output
            Iteration = ++Iteration % (4 * IterativeMultiplier); //Adjust by amount of idle tasks
            Log.Add("Update source: " + updateSource);
            Log.Add("Current Iteration: " + (Iteration + 1) + "/" + 4 * IterativeMultiplier);
            Log.Print(LCD);
        }


        public Program() {
            // The constructor, called only once every session and
            // always before any other method is called.
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            Log = new LogEngine(this, "UFOslava's DCM Lift Control");

            //update block records
            RescanBlocks();
            CurState = States[0]; //unknown
            LastState = States[0];
        }
    }

    public class LogEngine {
        private readonly MyGridProgram _program;
        private List<String> Messages = new List<string>();
        private List<String> Warnings = new List<string>();
        private List<String> Errors = new List<string>();
        private readonly string Prefix = "UFOslava's DCM Lift Control";
        private int Iteration = 0; //Log engine print iteration counter, for alive indicator
        private readonly string[] RunIndicatorStrings = new string[4] {"/", "--", "\\", "|"};
        public int Warning = 1;
        public int Error = 2;

        public LogEngine(MyGridProgram Program, string prefix) {
            _program = Program;
            Prefix = prefix;
        }

        public LogEngine(MyGridProgram Program) { _program = Program; }

        public void Add(string Content, int Type) {
            switch (Type) {
                case 1:
                    Warnings.Add(Content);
                    break;
                case 2:
                    Errors.Add(Content);
                    break;
                default:
                    Messages.Add(Content);
                    break;
            }
        }

        public void Add(string Content) { Messages.Add(Content); }

        private string RunIndicator() {
            Iteration = Iteration % 4;
            return " " + RunIndicatorStrings[Iteration++];
        }

        public void Print(List<IMyTextSurface> LogScreens) {
            string Output = Prefix + RunIndicator() + "\n"; //Add default massage
            _program.Echo("Detected screens: " + LogScreens.Count);
            foreach (string Line in Messages) {
                Output += Line + "\n";
            }

            Messages = new List<string>(); //reset lists

            if (Warnings.Count > 0) {
                Output += "\nWarnings:\n";
                foreach (string Line in Warnings) {
                    Output += Line + "\n";
                }
            }

            Warnings = new List<string>();

            if (Errors.Count > 0) {
                Output += "\nErrors:\n";
                foreach (string Line in Errors) {
                    Output += Line + "\n";
                }
            }

            Errors = new List<string>();

            if (LogScreens.Count <= 0) {
                Output += "No output screens detected.\n";
            }

            foreach (IMyTextSurface Screen in LogScreens) {
                try {
                    Screen.ContentType = ContentType.TEXT_AND_IMAGE;
                    Screen.WriteText(Output);
                } catch {
                    _program.Echo(Screen.Name + " is not a valid screen.");
                }
            }


            _program.Echo(Output);
        }
    }
}