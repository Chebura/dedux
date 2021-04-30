using System.Collections.Generic;
using ProtoBuf;

namespace dedux.Cache
{
    [ProtoContract]
    public class CacheDirectory
    {
        [ProtoMember(1)] public ICollection<CacheData> Data { get; set; }
    }
}