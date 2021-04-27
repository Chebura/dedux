using System.Collections.Generic;
using ProtoBuf;

namespace dedux.Cache
{
    [ProtoContract]
    public class CacheDirectory
    {
        [ProtoMember(1)] public ICollection<CacheData> Data { get; set; }

        [ProtoMember(2)] public string BaseDir { get; set; }

        [ProtoMember(3)] public string TargetPath { get; set; }
    }
}