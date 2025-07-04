// Settings.cs
using System.Collections.Generic;

namespace ProcessPurge
{
    public class AppSettings
    {
        public bool StartWithWindows { get; set; } = true;
        public bool MinimizeToTrayOnClose { get; set; } = true;
        public bool PoliteKill { get; set; } = true;
        public bool BlockCriticalProcesses { get; set; } = true;
        public bool ShowCpuPercentage { get; set; } = false; // NEW: Off by default for performance
        public bool EulaAccepted { get; set; } = false;
        public List<string> PurgeList { get; set; } = new List<string>();
    }
}
