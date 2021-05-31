using System.Collections.Generic;
using System.Xml.Serialization;


namespace PocketPCRController
{
    /// <summary>
    /// Class structure for defining themalcycling programs
    /// </summary>
    public class PCRPrograms
    {
        /// <summary>
        /// List of PCRProgram objects
        /// </summary>
        public List<PCRProgram> ProgramList { get; set; }
        /// <summary>
        /// Indexed, sorted dictionary of PCRProgram objects
        /// <para>
        /// [XmlIgnoreAttribute] declared because a SortedList cannot be serialized with a single command
        /// </para>
        /// </summary>
        [XmlIgnoreAttribute]
        public SortedList<string, PCRProgram> Programs { get; set; }
        
        /// <summary>
        /// Default constructor
        /// </summary>
        public PCRPrograms()
        {
            ProgramList = new List<PCRProgram>();
        }

        /// <summary>
        /// Build a SortedList of PCRProgram objects
        /// </summary>
        public void BuildDictionary()
        {
            Programs = new SortedList<string, PCRProgram>();
            foreach (PCRProgram currProgram in ProgramList)
            {
                if (!Programs.ContainsKey(currProgram.ProgramName))
                {
                    Programs.Add(currProgram.ProgramName, currProgram);
                }
            }
        }
    }

    /// <summary>
    /// A temperature and time block for a thermalcycling program
    /// </summary>
    public class PCRBlock
    {
        public double TargetTemperature { get; set; }
        public int TargetTimeSeconds { get; set; }
        public PCRBlock()
        {
            TargetTemperature = 0;
            TargetTimeSeconds = 0;
        }
        public PCRBlock(double _targetTemperature, int _targetTimeSeconds)
        {
            TargetTemperature = _targetTemperature;
            TargetTimeSeconds = _targetTimeSeconds;
        }
    }

    /// <summary>
    /// An open-ended series of themalcycling block steps that can be repeated an indefinite number of times
    /// </summary>
    public class PCRCycle
    {
        public List<PCRBlock> Blocks { get; set; }
        public int NumberOfCycles { get; set; }
        public PCRCycle()
        {
            NumberOfCycles = 0;
            Blocks = new List<PCRBlock>();
        }
        public int Add(double _targetTemperature, int _targetTimeSeconds)
        {
            PCRBlock newBlock = new PCRBlock(_targetTemperature, _targetTimeSeconds);
            Blocks.Add(newBlock);
            return Blocks.Count;
        }
        public int Add(PCRBlock _newBlock)
        {
            Blocks.Add(_newBlock);
            return Blocks.Count;
        }
    }

    /// <summary>
    /// A thermalcycling program consisting of a series of PCRCycle objects
    /// </summary>
    public class PCRProgram
    {
        public string ProgramName { get; set; }
        public List<PCRCycle> Cycles { get; set; }
        public int NumberOfCycles { get; set; }
        public PCRProgram()
        {
            ProgramName = "";
            Cycles = new List<PCRCycle>();
            NumberOfCycles = 0;
        }
        public PCRProgram(string _programName)
        {
            ProgramName = _programName;
            Cycles = new List<PCRCycle>();
            NumberOfCycles = 0;
        }

        public void RecountCycles()
        {
            NumberOfCycles = 0;
            for (int i = 0; i < Cycles.Count; i++)
            {
                NumberOfCycles += Cycles[i].NumberOfCycles;
            }
        }
    }
}
