﻿using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
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
using IMyCryoChamber = Sandbox.ModAPI.Ingame.IMyCryoChamber;
using IMyProgrammableBlock = Sandbox.ModAPI.Ingame.IMyProgrammableBlock;
using IMySensorBlock = Sandbox.ModAPI.Ingame.IMySensorBlock;
using IMyShipConnector = Sandbox.ModAPI.Ingame.IMyShipConnector;
using IMyTerminalBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using IMyTextSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;


namespace IngameScript {
    class Program : MyGridProgram {
        // Go to:
        // https://github.com/malware-dev/MDK-SE/wiki/Quick-Introduction-to-Space-Engineers-Ingame-Scripts
        //
        // to learn more about ingame scripts.
        public List<IMyTextSurface> OutputLcdList = new List<IMyTextSurface>();
        public List<IMyLandingGear> BottomLandingGearsList = new List<IMyLandingGear>();
        public List<IMyLandingGear> TopLandingGearsList = new List<IMyLandingGear>();
        public List<IMyShipConnector> TopConnector = new List<IMyShipConnector>();
        public List<IMyShipConnector> BottomConnector = new List<IMyShipConnector>();
        public List<IMyProgrammableBlock> CruiseControlProgrammableBlock = new List<IMyProgrammableBlock>();
        public List<IMySensorBlock> TopSensorBlock = new List<IMySensorBlock>();
        public List<IMySensorBlock> BottomSensorBlock = new List<IMySensorBlock>();
        public List<IMySensorBlock> PeopleSensorBlock = new List<IMySensorBlock>();
        public static List<IMyTerminalBlock> TerminalBlockList = new List<IMyTerminalBlock>(); //declare an empty list of TerminalBlocks for later use in searches.
        public static List<IMyTerminalBlock> TerminalBlockListCurrentGrid = new List<IMyTerminalBlock>(); // T:
        public Dictionary<string, string> SettingsDictionary = new Dictionary<string, string>() {{"Output LCD Name", "T:Status LCD"}, {"Top Floor Connector", "T:Top Connector"}, {"Bottom Floor Connector", "T:Bottom Connector"}, {"Cruise Control PB", "T:Cruise Control"}, {"Top Sensor", "T:Top Sensor"}, {"Bottom Sensor", "T:Bottom Sensor"}, {"Passengers Sensor","T:People Sensor" } };
        public LogEngine Log;
        public int Iteration = 0;//Counts script iterations for unfrequent task schedling and task load distribution.
        public int IterativeMultiplier = 1;//Compensates for script execution speed

        public List<IMyTerminalBlock> GetBlocksByPattern(string Pattern) { //Get AutoLCD2 type pattern, get back requested blocks, from current grids or otherwise.
            if (Pattern == null) {
                return TerminalBlockList; //return all on empty patern
            }

            List<IMyTerminalBlock> ReturnList = new List<IMyTerminalBlock>();
            if (Pattern.StartsWith("T:")) { //Return current grid Blocks only, by name.
                Pattern = Pattern.Substring(2); //Update pattern with T: removed.
                foreach (IMyTerminalBlock Block in TerminalBlockListCurrentGrid) {
                    if (Block.CustomName.Contains(Pattern))
                        ReturnList.Add(Block);
                }

                return ReturnList;
            }

            if (Pattern.StartsWith("G:")) { //Return all group Blocks
                GridTerminalSystem.GetBlockGroupWithName(Pattern.Substring(2)).GetBlocks(ReturnList);
                return ReturnList;
            }

            foreach (IMyTerminalBlock Block in TerminalBlockListCurrentGrid) {
                if (Block.CustomName.Contains(Pattern))
                    ReturnList.Add(Block);
            }

            return ReturnList;
        }

        //parse Custom Data
        public static Dictionary<string, string> ParseCustomData(IMyTerminalBlock Block, Dictionary<string, string> Settings) { //Get current CustomData and parse values requested by the dictionary.
            var CustomData = new Dictionary<string, string>(); //Original Data
            var CustomDataSettings = new Dictionary<string, string>(); //Parsed Data
            string[] CustomDataLines = Block.CustomData.Split(new char[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
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
            IMyTerminalBlock tempTerminalBlock;

            SettingsDictionary = ParseCustomData(Me, SettingsDictionary);

            TerminalBlockList = new List<IMyTerminalBlock>();//reset Block lists
            TerminalBlockListCurrentGrid = new List<IMyTerminalBlock>();

            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(TerminalBlockList); //Acquire all "Smart" blocks
            //Echo("Total terminal blocks in the grid: " + TerminalBlockList.Count + "\n");
            foreach (IMyTerminalBlock Block in TerminalBlockList) {
                if (FilterThis(Block))
                    TerminalBlockListCurrentGrid.Add(Block); //Get Blocks of current Grid.
            }

            try
            {
                //Find specific Blocks
                TopConnector = GetBlocksByPattern(SettingsDictionary["Top Floor Connector"]).Cast<IMyShipConnector>().ToList();
                if (TopConnector.Count <= 0)
                {
                    Log.Add("Top Floor Connector not found!",Log.Error);
                }
                BottomConnector = GetBlocksByPattern(SettingsDictionary["Bottom Floor Connector"]).Cast<IMyShipConnector>().ToList();
                if (BottomConnector.Count <= 0)
                {
                    Log.Add("Bottom Floor Connector not found!", Log.Error);
                }
                //Log.Add("Bottom Conector: " + BottomConnector.CustomName);
                CruiseControlProgrammableBlock = GetBlocksByPattern(SettingsDictionary["Cruise Control PB"]).Cast<IMyProgrammableBlock>().ToList();
                if (CruiseControlProgrammableBlock.Count <= 0)
                {
                    Log.Add("Cruise Control PB not found!", Log.Error);
                }
                //TODO add te rest of the validations

                TopSensorBlock = GetBlocksByPattern(SettingsDictionary["Top Sensor"]).Cast<IMySensorBlock>().ToList();
                //Log.Add("Top Sensor: " + TopSensorBlock.CustomName);
                BottomSensorBlock = GetBlocksByPattern(SettingsDictionary["Bottom Sensor"]).Cast<IMySensorBlock>().ToList();
                PeopleSensorBlock = GetBlocksByPattern(SettingsDictionary["Passengers Sensor"]).Cast<IMySensorBlock>().ToList();
                //Log.Add("Bottom Sensor: " + BottomSensorBlock.CustomName);
                //TODO scan landing gears by orientation
                //Output screens
                OutputLcdList = GetTextSurfaces("Output LCD Name");
            }
            catch (Exception e) {
                Log.Add(e.Message + "\n(in Blocks lookup)", Log.Error);
            }
            Log.Add("Blocks rescanned");
        }

        public List<IMyTextSurface> GetTextSurfaces(string pattern) {
            List<IMyTerminalBlock> OutputBlocks = GetBlocksByPattern(SettingsDictionary[pattern]);
            List<IMyTextSurface> OutputList = new List<IMyTextSurface>();
            foreach (IMyTerminalBlock block in OutputBlocks) {
                if (block == null) {
                    continue;
                }

                IMyTextSurfaceProvider provider = block as IMyTextSurfaceProvider;
                if (provider != null) {
                    if (provider.GetSurface(0) != null) {
                        try {
                            OutputList.Add(provider.GetSurface(0));
                        } catch (Exception e) {
                            Log.Add(e.ToString());
                        }
                    }


                    continue;
                }

                if (block is IMyTextSurface) {
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

        public void Save() {
            // Called when the program needs to save its state. Use
            // this method to save your state to the Storage field
            // or some other means. 
            // 
            // This method is optional and can be removed if not
            // needed.
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
            Log.Add("Update source: " + updateSource);
            Log.Add("Current Iteration: " + Iteration + "/" + 4 * IterativeMultiplier);
            if (argument.Length > 0)
            {
                Log.Add("Argument: " + argument);
            } else {
                Log.Add("No Argument.");
            }
            switch (updateSource) {
               
                case UpdateType.None:
                    break;
                case UpdateType.Terminal://Script call trough terminal window
                    //break;
                case UpdateType.Trigger://Script call
                    if (argument.ToLower().Contains("set100"))
                    {
                        Runtime.UpdateFrequency = UpdateFrequency.Update100;
                        break;
                    }
                    else if (argument.ToLower().Contains("set10"))
                    {
                        Runtime.UpdateFrequency = UpdateFrequency.Update10;
                        break;
                    }
                    else if (argument.ToLower().Contains("set1"))
                    {
                        Runtime.UpdateFrequency = UpdateFrequency.Update1;
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
                    Update://"goto" landing
                    Iteration = ++Iteration % (4*IterativeMultiplier);//Adjust by amount of idle tasks
                    switch (Iteration) {
                        case 0://rescan blocks
                            RescanBlocks();
                            break;
                        default:
                            break;
                    }
                    break;
                case UpdateType.Once:
                    break;
                case UpdateType.IGC:
                    break;
            }
            Log.Print(OutputLcdList);
        }


        public Program() {
            // The constructor, called only once every session and
            // always before any other method is called. Use it to
            // initialize your script. 
            //     
            // The constructor is optional and can be removed if not
            // needed.
            // 
            // It's recommended to set Runtime.UpdateFrequency 
            // here, which will allow your script to run itself without a 
            // timer block.
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            Log = new LogEngine(this, "UFOslava's DCM Lift Control");

            //update block records
            Dictionary<string, string> customData = ParseCustomData(Me, SettingsDictionary); //Scan my Custom Data
            RescanBlocks();
        }
    }

    public class LogEngine {
        private readonly MyGridProgram _program;
        private List<String> Messages = new List<string>();
        private List<String> Warnings = new List<string>();
        private List<String> Errors = new List<string>();
        private string Prefix = "UFOslava's DCM Lift Control";
        private int Iteration = 0;//Log engine print iteration counter, for alive indicator
        private readonly string[] RunIndicatorStrings = new string[4] { "/", "--", "\\", "|" };  
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

            if (Warnings.Count > 0)
            {
                Output += "\nWarnings:\n";
                foreach (string Line in Warnings) {
                    Output += Line + "\n";
                }
            }
            
            Warnings = new List<string>();

            if (Errors.Count > 0)
            {
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
                try
                {
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