namespace ProjectAbyss.Core.Net
{
    /// <summary>Transport for single-player: the local host session without any network peers.</summary>
    public sealed class NullTransport : INetTransport
    {
        public void Send(long peer, byte[] data, bool reliable)
        {
        }

        public void Disconnect(long peer)
        {
        }

        public bool TryReceive(out TransportEvent e)
        {
            e = default;
            return false;
        }
    }
}
