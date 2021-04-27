using System.Collections.Generic;

namespace dedux.Dedux
{
    public class DeduxCache
    {
        public IDictionary<string, IList<DeduxCache>> _i { get; set; }
    }
}