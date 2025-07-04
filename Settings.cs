using System.Collections.Generic;

namespace ProcessPurge
{
    // Settings.cs
    public class AppSettings
    {
        public bool StartWithWindows { get; set; } = true;
        public bool MinimizeToTrayOnClose { get; set; } = true;
        public bool PoliteKill { get; set; } = true;
        public bool BlockCriticalProcesses { get; set; } = true; // <-- ADD THIS LINE
        public bool EulaAccepted { get; set; } = false;
        public List<string> PurgeList { get; set; } = new List<string>();
    }
}