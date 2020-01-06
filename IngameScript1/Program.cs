
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using System.Diagnostics;
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
        // Set MagLev distance to 1.22m
        // Set MagLev power to 4M KN
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

        /// <summary>Top Cruise Velocity</summary>
        public float TCVel;

        /// <summary>Bottom Cruise Velocity</summary>
        public float BCVel;

        /// <summary>Top Approach Velocity</summary>
        public float TAVel;

        /// <summary>Bottom Approach Velocity</summary>
        public float BAVel;

        /// <summary>Parking Velocity</summary>
        public float PVel;

        public List<IMySensorBlock> TASens; //Top facing approach sensor
        public float TADist; //Top Approach Distance
        public List<IMySensorBlock> TPSens; //Top facing approach sensor
        public float TPDist; //Top Parking Distance
        public List<IMySensorBlock> BASens; //Bottom facing parking sensor
        public float BADist; //Bottom Approach Distance
        public List<IMySensorBlock> BPSens; //Bottom facing parking sensor
        public float BPDist; //bottom Parking Distance
        public List<IMySensorBlock> PSens; //Cabin Sensor (People)
        public List<IMyDoor> TDoor = new List<IMyDoor>(); //Top Doors
        public List<IMyDoor> BDoor = new List<IMyDoor>(); //Bottom Doors
        public List<IMyBatteryBlock> Batt = new List<IMyBatteryBlock>(); //Batteries
        public List<IMyThrust> Thr = new List<IMyThrust>(); //Thrusters

        public static List<IMyTerminalBlock> TerminalBlockList = new List<IMyTerminalBlock>(); //declare an empty list of TerminalBlocks for later use in searches.
        public static List<IMyTerminalBlock> TerminalBlockListCurrentGrid = new List<IMyTerminalBlock>(); // "T:"

        //utility vars
        public bool RescanBlocksSuccess; //Script indicator that all essential blocks are found.

        public Dictionary<string, string> SettingsDictionary = new Dictionary<string, string>() {
            {"Debug Print", "false"},
            {"Output LCD", "T:Status"},
            {"Top Floor Connector", "T:Top Connector"},
            {"Bottom Floor Connector", "T:Bottom Connector"},
            {"Cruise Control PB", "T:Cruise Control"},
            {"Top Approach Sensor", "T:Top Approach"},
            {"Top Parking Sensor", "T:Top Parking"},
            {"Bottom Approach Sensor", "T:Bottom Approach"},
            {"Bottom Parking Sensor", "T:Bottom Parking"},
            {"Passengers Sensor", "T:People Sensor"},
            {"Top Floor Doors", "Top Floor"},
            {"Bottom Floor Doors", "Bottom Floor"},
            {"Landing Gear", "T:Landing Gear"},
            {"Batteries", "T:*"},
            {"Thrusters", "T:*"},
            {"Upward Cruise Speed [m/s]", "50"},
            {"Downward Cruise Speed [m/s]", "50"},
            {"Upward Approach Distance [1.5m~50m]", "50"},
            {"Downward Approach Distance [1.5m~50m]", "50"},
            {"Upward Approach Speed [m/s]", "10"},
            {"Downward Approach speed [m/s]", "10"},
            {"Upward Parking Distance [1.5m~50m]", "10"},
            {"Downward Parking Distance [1.5m~50m]", "10"},
            {"Parking Speed [m/s]", "2"}
        };

        public LogEngine Log;

        public bool DebugMode;

        /// <summary>
        /// Main state machine - current state
        /// </summary>
        public string CurState;

        public string CurStateTemp; //temporary state storage to workaround the last state dependent actions in main state machine
        public string LastState;

        /// <summary>
        /// The direction the lift commanded to go
        /// </summary>
        private string LiftIntent;

        public string[] States = {
            "Unknown",
            "Idle",
            "PrepDep", //Prepare for Departure

            "Cruise", //Cruise downwards
            "Approach", //Approach bottom floor
            "Parking", //Parking at bottom floor
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
                    CustomDataSettings.Add(Setting.Key, Setting.Value);
                    Block.CustomData += "\n" + Setting.Key + " = " + Setting.Value;
                }
            }

            return CustomDataSettings;
        }

        public bool FilterThis(IMyTerminalBlock block) { return block.CubeGrid == Me.CubeGrid; }

        void RescanBlocks() {
            SettingsDictionary = ParseCustomData(Me, SettingsDictionary);

            DebugMode = SettingsDictionary["Debug Print"].ToLower().Contains("true");

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
                RescanBlocksSuccess = TConn.Count > 0 ? EnableBlockList(TConn) : false;
                BConn = GetSpecificTypeBlocksByPattern<IMyShipConnector>("Bottom Floor Connector");
                RescanBlocksSuccess = BConn.Count > 0 ? EnableBlockList(BConn) : false;
                CCPB = GetSpecificTypeBlocksByPattern<IMyProgrammableBlock>("Cruise Control PB");
                RescanBlocksSuccess = CCPB.Count > 0 ? EnableBlockList(CCPB) : false;
                TASens = GetSpecificTypeBlocksByPattern<IMySensorBlock>("Top Approach");
                RescanBlocksSuccess = TASens.Count > 0 ? EnableBlockList(TASens) : false;
                TPSens = GetSpecificTypeBlocksByPattern<IMySensorBlock>("Top Parking");
                RescanBlocksSuccess = TPSens.Count > 0 ? EnableBlockList(TPSens) : false;
                BASens = GetSpecificTypeBlocksByPattern<IMySensorBlock>("Bottom Approach");
                RescanBlocksSuccess = BASens.Count > 0 ? EnableBlockList(BASens) : false;
                BPSens = GetSpecificTypeBlocksByPattern<IMySensorBlock>("Bottom Parking");
                RescanBlocksSuccess = BPSens.Count > 0 ? EnableBlockList(BPSens) : false;
                PSens = GetSpecificTypeBlocksByPattern<IMySensorBlock>("Passengers Sensor");
                RescanBlocksSuccess = PSens.Count > 0 ? EnableBlockList(PSens) : false;
                LCD = GetTextSurfaces("Output LCD"); //Output screens
                TDoor = GetSpecificTypeBlocksByPattern<IMyDoor>("Top Floor Doors");
                RescanBlocksSuccess = TDoor.Count > 0 ? EnableBlockList(TDoor) : false;
                BDoor = GetSpecificTypeBlocksByPattern<IMyDoor>("Bottom Floor Doors");
                RescanBlocksSuccess = BDoor.Count > 0 ? EnableBlockList(BDoor) : false;
                LG = GetSpecificTypeBlocksByPattern<IMyLandingGear>("Landing Gear");
                RescanBlocksSuccess = LG.Count > 0 ? EnableBlockList(LG) : false;
                Batt = GetSpecificTypeBlocksByPattern<IMyBatteryBlock>("Batteries");
                RescanBlocksSuccess = Batt.Count > 0 ? EnableBlockList(Batt) : false;
                Thr = GetSpecificTypeBlocksByPattern<IMyThrust>("Thrusters");
                //EnableBlockList(Thr);
            } catch (Exception e) {
                Log.Add(e.Message + "\n(in Blocks lookup)", Log.Error);
                RescanBlocksSuccess = false;
            }

            TADist = Math.Abs(float.Parse(SettingsDictionary["Upward Approach Distance [1.5m~50m]"]));
            BADist = Math.Abs(float.Parse(SettingsDictionary["Downward Approach Distance [1.5m~50m]"]));
            TPDist = Math.Abs(float.Parse(SettingsDictionary["Upward Parking Distance [1.5m~50m]"]));
            BPDist = Math.Abs(float.Parse(SettingsDictionary["Downward Parking Distance [1.5m~50m]"]));
            PVel = Math.Abs(float.Parse(SettingsDictionary["Parking Speed [m/s]"]));
            TAVel = Math.Abs(float.Parse(SettingsDictionary["Upward Approach Speed [m/s]"]));
            BAVel = Math.Abs(float.Parse(SettingsDictionary["Downward Approach speed [m/s]"]));
            TCVel = Math.Abs(float.Parse(SettingsDictionary["Upward Cruise Speed [m/s]"]));
            BCVel = Math.Abs(float.Parse(SettingsDictionary["Downward Cruise Speed [m/s]"]));

            if (RescanBlocksSuccess) {
                Log.Add("Blocks rescanned success.");
            } else {
                Log.Add("Blocks rescan failed!");
            }
        }

        public List<T> GetSpecificTypeBlocksByPattern<T>(string dicIndex) where T : IMyTerminalBlock {
            if (DebugMode)
                Log.Add("Pattern search started/nlooking for" + dicIndex + "/nof type" + typeof(T));
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

            if (DebugMode)
                Log.Add("Found " + Temp.Count + " items.");
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

        public bool EnableBlockList<T>(List<T> BlockList, bool State = true) where T : IMyFunctionalBlock {
            bool Success = false; //assume failure result
            foreach (var block in BlockList) {
                block.Enabled = State;
                Success = true; //Any block enabled
            }

            return Success;
        }

        public void Save() {
            // Called when the program needs to save its state. Use
            // this method to save your state to the Storage field
            // or some other means. 
            // 
            // This method is optional and can be removed if not
            // needed.
        }

        private void ConfigBlocks() { //enable all relevant blocks
            try {
                EnableBlockList(Batt);
                EnableBlockList(Thr);
                EnableBlockList(LG);
                EnableBlockList(TConn);
                EnableBlockList(BConn);
                EnableBlockList(CCPB);
                EnableBlockList(TPSens);
                EnableBlockList(BPSens);
                EnableBlockList(PSens);
                //EnableBlockList(TDoor);
                //EnableBlockList(BDoor);
                Log.Add("Blocks enabled.");
            } catch (Exception e) {
                Log.Add(e.Message, Log.Error);
            }
        }

        public bool SetSensors<T>(List<T> Sensors, float Dist = 50f) where T : IMySensorBlock {
            Dist = Math.Min(50f, Math.Max(1.5f, Dist)); // 1.5~50
            try {
                Sensors.First().Enabled = true;

                Sensors.First().FrontExtend = Dist;
                Sensors.First().BackExtend = 2.5f;
                Sensors.First().LeftExtend = 2.5f;
                Sensors.First().RightExtend = 2.5f;
                Sensors.First().TopExtend = 2.5f;
                Sensors.First().BottomExtend = 2.5f;

                Sensors.First().ApplyAction("Detect Players_Off");
                Sensors.First().ApplyAction("Detect Floating Objects_Off");
                Sensors.First().ApplyAction("Detect Small Ships_On");
                Sensors.First().ApplyAction("Detect Large Ships_On");
                Sensors.First().ApplyAction("Detect Stations_On");
                Sensors.First().ApplyAction("Detect Asteroids_Off");

                Sensors.First().ApplyAction("Detect Owner_On");
                Sensors.First().ApplyAction("Detect Friendly_On");
                Sensors.First().ApplyAction("Detect Neutral_On");
                Sensors.First().ApplyAction("Detect Enemy_On");
            } catch (Exception e) {
                return false;
            }

            return true;
        }

        public bool SetPassengerSensors<T>(List<T> Sensors) where T : IMySensorBlock {
            try {
                Sensors.First().Enabled = true;

                Sensors.First().ApplyAction("Detect Players_On");
                Sensors.First().ApplyAction("Detect Floating Objects_Off");
                Sensors.First().ApplyAction("Detect Small Ships_Off");
                Sensors.First().ApplyAction("Detect Large Ships_Off");
                Sensors.First().ApplyAction("Detect Stations_Off");
                Sensors.First().ApplyAction("Detect Asteroids_Off");

                Sensors.First().ApplyAction("Detect Owner_On");
                Sensors.First().ApplyAction("Detect Friendly_On");
                Sensors.First().ApplyAction("Detect Neutral_On");
                Sensors.First().ApplyAction("Detect Enemy_Off");
            } catch (Exception e) {
                return false;
            }

            return true;
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
                    switch (argument.ToLower()) {
                        case "up":
                            LiftIntent = "up";
                            CurState = "PrepDep";
                            break;
                        case "down":
                        case "dn":
                            LiftIntent = "down";
                            CurState = "PrepDep";
                            break;
                    }

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
                            //RescanBlocks();
                            //Iteration = RescanBlocksSuccess ? Iteration : 3;
                            break;
                        case 1:
                        //ConfigBlocks();
                        //break;
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

//            float Dist = Math.Min(50f, Math.Max(1.5f, float.Parse(SettingsDictionary["Upward Approach Distance [1.5m~50m]"])));// 1.5~50
//            Log.Add("Sensor distance: " + Dist);

            //Main state machine
            CurStateTemp = CurState;
            switch (CurState) {
                case "Unknown":
                    Runtime.UpdateFrequency = UpdateFrequency.Update100;
                    RescanBlocks();
                    CurState = "PrepDep";
                    LiftIntent = "up";

                    //check sensors, go to nearest floor
                    //Approach
                    if (SetSensors(TASens, TADist) && SetSensors(BASens, BADist)) {
                        if (BASens.First().IsActive) {
                            CurState = "Approach";
                            LiftIntent = "down";
                        }

                        if (TASens.First().IsActive) {
                            CurState = "Approach";
                            LiftIntent = "up";
                        }
                    }

                    //check sensors, go to nearest floor
                    //Parking
                    if (SetSensors(TPSens, 50) && SetSensors(BPSens, 50)) {
                        if (BPSens.First().IsActive) {
                            CurState = "Parking";
                            LiftIntent = "down";
                        }

                        if (TPSens.First().IsActive) {
                            CurState = "Parking";
                            LiftIntent = "up";
                        }
                    }

//                    foreach (var gear in LG) {
//                        if (gear.LockMode != LandingGearMode.Unlocked) {
//                            CurState = "Unknown";
//                        }
//                    }

                    foreach (var Con in TConn) {
                        if (Con.Status != MyShipConnectorStatus.Unconnected) {
                            CurState = "Parking";
                            LiftIntent = "up";
                            break;
                        }
                    }

                    foreach (var Con in BConn) {
                        if (Con.Status != MyShipConnectorStatus.Unconnected) {
                            CurState = "Parking";
                            LiftIntent = "down";
                            break;
                        }
                    }

                    break;
                case "Idle":
                    Runtime.UpdateFrequency = UpdateFrequency.Update100;
                    RescanBlocks();

                    EnableBlockList(Batt); //recharge if connected
                    if (TConn.First().Status == MyShipConnectorStatus.Connected || BConn.First().Status == MyShipConnectorStatus.Connected) {
                        foreach (var batt in Batt)
                            batt.ChargeMode = ChargeMode.Recharge;
                    }


                    if (LastState != CurState) { //State running first time.
                        EnableBlockList(BDoor);
                        EnableBlockList(TDoor);
                        EnableBlockList(PSens);
                        SetPassengerSensors(PSens);
                        EnableBlockList(Thr, false);
                    }

                    foreach (var gear in LG)
                        gear.Lock();
                    foreach (var Conn in TConn)
                        Conn.Connect();
                    foreach (var Conn in BConn)
                        Conn.Connect();

                    if (SetPassengerSensors(PSens)) { //Make sure blocks exist before using first()
                        if (PSens.First().IsActive) { //People inside? Open doors.
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
                    }

                    //Final destination - no natural migrating from this case.
                    break;

                case "PrepDep": //Prepare for Departure
                    Runtime.UpdateFrequency = UpdateFrequency.Update10;
                    if (LastState != CurState) {
                        EnableBlockList(Batt);
                        EnableBlockList(Thr);
                        EnableBlockList(TDoor);
                        EnableBlockList(BDoor);
                        EnableBlockList(CCPB);
                        CCPB.First()?.TryRun("cc_on"); // Turn on CC
                        CCPB.First()?.TryRun("axis_v"); //Set to vertical


                        EnableBlockList(LiftIntent == "up" ? TPSens : BPSens); //Parking
                        EnableBlockList(LiftIntent == "up" ? TASens : BASens); //Approach
                    }

                    CCPB.First()?.TryRun("setspeed " + (LiftIntent == "up" ? "0" : "10")); //Hold in place in preparation for departure

                    bool doorsClosed = true; //Assumption that neated later via AND gate.
                    foreach (var batt in Batt) {
                        batt.ChargeMode = ChargeMode.Auto;
                    }


                    if (PSens.First().IsActive) { //People in the vicinity, open correct doors
                        if (TConn.First().Status != MyShipConnectorStatus.Unconnected) { //Is on top
                            foreach (var door in TDoor) {
                                door.OpenDoor();
                            }
                        } else if (BConn.First().Status != MyShipConnectorStatus.Unconnected) { //Is on botom
                            foreach (var door in BDoor) {
                                door.OpenDoor();
                            }
                        } else {
                            //CurState = "Unknown";
                        }

                        doorsClosed = false; //Hold departure
                    } else {
                        foreach (var door in TDoor) {
                            door.CloseDoor();
                            doorsClosed &= (door.Status == DoorStatus.Closed);
                        }

                        foreach (var door in BDoor) {
                            door.CloseDoor();
                            doorsClosed &= (door.Status == DoorStatus.Closed);
                        }
                    }

                    if (doorsClosed) {
                        CurState = "Cruise";
                        foreach (var connector in TConn)
                            connector.Disconnect();
                        foreach (var connector in BConn)
                            connector.Disconnect();
                        foreach (var lg in LG) {
                            lg.AutoLock = false;
                            lg.Unlock();
                            lg.Enabled = false;
                        }
                    }

                    break;

                case "Cruise": //Cruise downwards
                    if (LastState != CurState) {
                        Runtime.UpdateFrequency = UpdateFrequency.Update10;

                        EnableBlockList(Thr);
                        EnableBlockList(Batt);
                        EnableBlockList(CCPB);
                        EnableBlockList(TDoor, false);
                        EnableBlockList(BDoor, false);
                        foreach (var batteryBlock in Batt) {
                            batteryBlock.ChargeMode = ChargeMode.Auto;
                        }

                        EnableBlockList(LG);
                        foreach (var gear in LG) {
                            gear.AutoLock = false;
                            gear.Unlock();
                            gear.Enabled = false;
                        }

                        foreach (var conn in TConn)
                            conn.Disconnect();
                        foreach (var conn in BConn)
                            conn.Disconnect();

                        SetSensors(TASens, TADist);
                        SetSensors(BASens, BADist);
                        SetSensors(TPSens, TPDist);
                        SetSensors(BPSens, BPDist);
                        SetPassengerSensors(PSens);

                        CCPB.First()?.TryRun("cc_on"); // Turn on CC
                        CCPB.First()?.TryRun("axis_v"); //Set to vertical
                        CCPB.First()?.TryRun("setspeed " + (LiftIntent == "up" ? TCVel : -BCVel));
                    }

                    if (PSens.First().IsActive) { //stop if humans detected
                        CCPB.First()?.TryRun("setspeed 0");
                    } else {
                        CCPB.First()?.TryRun("setspeed " + (LiftIntent == "up" ? TCVel : -BCVel));
                    }

                    if (LiftIntent == "up" && TASens.First().IsActive)
                        CurState = "Approach";
                    if (LiftIntent == "down" && BASens.First().IsActive)
                        CurState = "Approach";

                    break;

                case "Approach": //Approach bottom floor
                    Runtime.UpdateFrequency = UpdateFrequency.Update10;

                    if (LastState != CurState) {
                        SetSensors(TPSens, TPDist);
                        SetSensors(BPSens, BPDist);
                        SetSensors(TASens, TADist);
                        SetSensors(BASens, BADist);
                        EnableBlockList(Thr);
                        EnableBlockList(CCPB);
                        EnableBlockList(TConn);
                        EnableBlockList(BConn);
                        EnableBlockList(LG);

                        EnableBlockList(TDoor);
                        EnableBlockList(BDoor);
                        foreach (var door in TDoor)
                            door.CloseDoor();
                        foreach (var door in BDoor)
                            door.CloseDoor();
                    } else {
                        if (LiftIntent == "up" && TPSens.First().IsActive ||
                            LiftIntent == "down" && BPSens.First().IsActive) {
                            //Is active AND in right direction?
                            CurState = "Parking";
                        }
                    }

                    CCPB.First()?.TryRun("setspeed " + (LiftIntent == "up" ? TAVel : -BAVel));
                    break;

                case "Parking": //Parking at bottom floor
                    if (LastState != CurState) {
                        EnableBlockList(Thr);
                        EnableBlockList(CCPB);
                        EnableBlockList(TConn);
                        EnableBlockList(BConn);
                        EnableBlockList(LG);
                        EnableBlockList(TDoor);
                        EnableBlockList(BDoor);
                        foreach (var door in TDoor)
                            door.CloseDoor();
                        foreach (var door in BDoor)
                            door.CloseDoor();
                    }


                    Runtime.UpdateFrequency = UpdateFrequency.Update10;
                    CCPB.First()?.TryRun("setspeed " + (LiftIntent == "up" ? PVel : -PVel)); //Set parking speed to a crawl, to prevent collision damage

                    bool LG_Ready = false;
                    bool Con_Ready = false;
                    bool Con_Locked = false;

                    foreach (var gear in LG) {
                        gear.AutoLock = true;
                        if (gear.LockMode != LandingGearMode.Unlocked) {
                            LG_Ready = true;
                            break;
                        }
                    }

                    if (LG_Ready) {
                        foreach (var Con in BConn) {
                            if (Con.Status != MyShipConnectorStatus.Unconnected) {
                                Con_Ready = true;
                                if (Con.Status == MyShipConnectorStatus.Connected)
                                    Con_Locked = true;
                                break;
                            }
                        }

                        foreach (var Con in TConn) {
                            if (Con.Status != MyShipConnectorStatus.Unconnected) {
                                Con_Ready = true;
                                if (Con.Status == MyShipConnectorStatus.Connected)
                                    Con_Locked = true;
                                break;
                            }
                        }
                    } else { //keep conectors disconnected until LG is locked
                        foreach (var Con in TConn)
                            Con.Disconnect();
                        foreach (var Con in BConn)
                            Con.Disconnect();
                    }

                    if (LG_Ready && Con_Ready) {
                        foreach (var gear in LG)
                            gear.Lock();
                        foreach (var Con in TConn)
                            Con.Connect();
                        foreach (var Con in BConn)
                            Con.Connect();

                        if (Con_Locked) {
                            CCPB.First()?.TryRun("cc_off");
                            //EnableBlockList(Thr, false);
                            foreach (var batteryBlock in Batt)
                                batteryBlock.ChargeMode = ChargeMode.Recharge;
                            CurState = "Idle";
                        }
                    }

                    break;

                default:
                    Log.Add("Unknown state: " + CurState, Log.Error);
                    break;
            }

            LastState = CurStateTemp;

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