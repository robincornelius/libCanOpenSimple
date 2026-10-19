using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace libCanopenSimple
{
    /// <summary>Device values read together under the node's configuration lease.</summary>
    public sealed class PdoConfigurationSnapshot
    {
        public ushort CommunicationIndex { get; private set; }
        public IReadOnlyDictionary<byte, byte[]> Communication { get; private set; }
        public IReadOnlyDictionary<byte, string> Unavailable { get; private set; }
        public IReadOnlyList<uint> Mappings { get; private set; }
        public string MappingError { get; private set; }

        public PdoConfigurationSnapshot(ushort communicationIndex, IDictionary<byte, byte[]> communication,
            IEnumerable<uint> mappings, IDictionary<byte, string> unavailable, string mappingError = null)
        {
            CommunicationIndex = communicationIndex;
            Communication = new ReadOnlyDictionary<byte, byte[]>(communication.ToDictionary(pair => pair.Key, pair => (byte[])pair.Value.Clone()));
            Unavailable = new ReadOnlyDictionary<byte, string>(new Dictionary<byte, string>(unavailable));
            Mappings = Array.AsReadOnly(mappings.ToArray());
            MappingError = mappingError;
        }
    }
}
