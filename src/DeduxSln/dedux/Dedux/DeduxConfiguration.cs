using System;

namespace dedux.Dedux
{
    public class DeduxConfiguration
    {
        public string SourceDir { get; set; }

        public string TargetDir { get; set; }

        public string CachePath { get; set; }

        public string DuplicatesPath { get; set; }

        public TimeSpan? ExecutionTimeout { get; set; }
    }
}