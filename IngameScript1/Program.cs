using Sandbox.Game.EntityComponents;
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
        // This file contains your actual script.
        //
        // You can either keep all your code here, or you can create separate
        // code files to make your program easier to navigate while coding.
        //
        // In order to add a new utility class, right-click on your project, 
        // select 'New' then 'Add Item...'. Now find the 'Space Engineers'
        // category under 'Visual C# Items' on the left hand side, and select
        // 'Utility Class' in the main area. Name it in the box below, and
        // press OK. This utility class will be merged in with your code when
        // deploying your final script.
        //
        // You can also simply create a new utility class manually, you don't
        // have to use the template if you don't want to. Just do so the first
        // time to see what a utility class looks like.
        // 
        // Go to:
        // https://github.com/malware-dev/MDK-SE/wiki/Quick-Introduction-to-Space-Engineers-Ingame-Scripts
        //
        // to learn more about ingame scripts.
        public List<IMyTextSurface> outputLcdList;
        public List<IMyLandingGear> bottomLandingGearsList;
        public List<IMyLandingGear> topLandingGearsList;
        public IMyShipConnector BottomConnector = null;
        public IMyShipConnector TopConnector = null;
        public IMyProgrammableBlock CruiseControlProgrammableBlock = null;
        public IMySensorBlock TopSensorBlock = null;
        public IMySensorBlock BottomSensorBlock = null;
        public static List<IMyTerminalBlock> TerminalBlockList = new List<IMyTerminalBlock>(); //declare an empty list of TerminalBlocks for later use in searches.
        public static List<IMyTerminalBlock> TerminalBlockListCurrentGrid = new List<IMyTerminalBlock>(); // T:
        public Dictionary<string, string> SettingsDictionary = new Dictionary<string, string>() {{"Output LCD Name", "T:Status LCD"}, {"Top Floor Connector", "T:Top Connector"}, {"Bottom Floor Connector", "T:Bottom Connector"}, {"Cruise Control PB", "T:Cruise Control"}, {"Top Sensor", "T:Top Sensor"}, {"Bottom Sensor", "T:Bottom Sensor"}};
        public LogEngine Log;

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
            try { //reset Block lists
                SettingsDictionary = ParseCustomData(Me, SettingsDictionary);
                TerminalBlockList = new List<IMyTerminalBlock>();
                TerminalBlockListCurrentGrid = new List<IMyTerminalBlock>();
                GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(TerminalBlockList); //Acquire all "Smart" blocks
                foreach (IMyTerminalBlock Block in TerminalBlockList) {
                    if (FilterThis(Block))
                        TerminalBlockListCurrentGrid.Add(Block); //Get Blocks of current Grid.
                }

                //Find specific Blocks
                TopConnector = (IMyShipConnector) GetBlocksByPattern(SettingsDictionary["Top Floor Connector"])[0];
                Log.Add("Top Conector: " + TopConnector.CustomName);
                BottomConnector = (IMyShipConnector) GetBlocksByPattern(SettingsDictionary["Bottom Floor Connector"])[0];
                Log.Add("Bottom Conector: " + BottomConnector.CustomName);
                CruiseControlProgrammableBlock = (IMyProgrammableBlock) GetBlocksByPattern(SettingsDictionary["Cruise Control PB"])[0];
                Log.Add("CC: " + CruiseControlProgrammableBlock.CustomName);
                TopSensorBlock = (IMySensorBlock) GetBlocksByPattern(SettingsDictionary["Top Sensor"])[0];
                Log.Add("Top Sensor: " + TopSensorBlock.CustomName);
                BottomSensorBlock = (IMySensorBlock) GetBlocksByPattern(SettingsDictionary["Bottom Sensor"])[0];
                Log.Add("Bottom Sensor: " + BottomSensorBlock.CustomName);
                //TODO scan landing gears by orientation
                //Output screens
                outputLcdList = GetTextSurfaces("Output LCD Name");
            } catch (Exception e) {
                Log.Add(e.Message, Log.Error);
            }
        }

        public List<IMyTextSurface> GetTextSurfaces(string pattern) {
            List<IMyTerminalBlock> OutputBlocks = GetBlocksByPattern(SettingsDictionary[pattern]);
            List<IMyTextSurface> OutputList = new List<IMyTextSurface>();
            foreach (IMyTerminalBlock block in OutputBlocks) {
                if (block == null) {
                    continue;
                }

                if (block is IMyTextSurfaceProvider && !(block is IMyCryoChamber)) {
                    try {
                        OutputList.Add(((IMyTextSurfaceProvider) block).GetSurface(0));
                    } catch (Exception e) {
                        Log.Add(e.ToString());
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
            switch (updateSource) {
                case UpdateType.None:
                    break;
                case UpdateType.Terminal:
                    break;
                case UpdateType.Trigger:
                    break;
                case UpdateType.Antenna:
                    break;
                case UpdateType.Mod:
                    break;
                case UpdateType.Script:
                    break;
                case UpdateType.Update1:
                    break;
                case UpdateType.Update10:
                    break;
                case UpdateType.Update100:
                    break;
                case UpdateType.Once:
                    break;
                case UpdateType.IGC:
                    break;
            }

            Log.Add("Update source: " + updateSource);
            RescanBlocks();
            Log.Print(outputLcdList);
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
        private int Iteration = 0;
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
            string Indicator = "/";
            Iteration = ++Iteration % 4;
            switch (Iteration) {
                case 1:
                    Indicator = "/";
                    break;
                case 2:
                    Indicator = "--";
                    break;
                case 3:
                    Indicator = @"\";
                    break;
                default:
                    Indicator = "|";
                    break;
            }

            return " " + Indicator;
        }

        public void Print(List<IMyTextSurface> LogScreens) {
            if (LogScreens == null)
                throw new ArgumentNullException(nameof(LogScreens));
            string Output = Prefix + RunIndicator() + "\n"; //Add default massage
            foreach (string Line in Messages) {
                Output += Line + "\n";
            }

            Output += "\nWarnings:\n";
            foreach (string Line in Warnings) {
                Output += Line + "\n";
            }

            Output += "\nErrors:\n";
            foreach (string Line in Errors) {
                Output += Line + "\n";
            }

            foreach (IMyTextSurface Screen in LogScreens) {
                Screen.WriteText(Output);
                _program.Echo(Output);
            }

            Messages = new List<string>(); //reset lists
            Warnings = new List<string>();
            Errors = new List<string>();
        }
    }
}