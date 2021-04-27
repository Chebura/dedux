using ProtoBuf;

namespace dedux.Cache
{
    [ProtoContract]
    public class CacheData
    {
        [ProtoMember(1)] public byte[] NameHash { get; set; }

        [ProtoMember(2)] public long Timestamp { get; set; }

        [ProtoMember(3)] public byte[] BodyHash { get; set; }

        [ProtoMember(4)] public string Path { get; set; }

    }
}