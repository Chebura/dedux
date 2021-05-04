using System;
using System.Collections.Generic;

namespace dedux.Dedux
{
    public class DeduxConfiguration
    {
        public ICollection<string> TargetDirs { get; set; }

        public string TargetDirSearchPattern { get; set; } = "*";

        public string CachePath { get; set; }

        public string DuplicatesPath { get; set; }

        public TimeSpan? ExecutionTimeout { get; set; }

        public bool DuplicateDelete { get; set; }

        public ICollection<string> DeletingMasks { get; set; }
    }
}