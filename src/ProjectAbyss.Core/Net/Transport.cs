using System;
using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Randomness;

namespace ProjectAbyss.Core.Net
{
    public enum TransportEventType
    {
        Connected,
        Data,
        Disconnected,
    }

    /// <summary>
    /// A transport-level event. <see cref="Peer"/> is a connection handle only; it is never trusted as identity
    /// (identity is proven in the handshake, see <see cref="ParticipantIdentity"/>).
    /// </summary>
    public readonly record struct TransportEvent(TransportEventType Type, long Peer, byte[]? Data);

    /// <summary>
    /// Minimal message transport used by the sessions. Implementations: <see cref="InMemoryNetwork"/> (tests, with
    /// latency/loss/duplication), <see cref="TcpTransport"/> (direct connections) and the SMAPI mod-message adapter.
    /// </summary>
    public interface INetTransport
    {
        /// <summary>Unreliable sends may be dropped; reliable sends arrive once and in order while connected.</summary>
        void Send(long peer, byte[] data, bool reliable);
        void Disconnect(long peer);
        bool TryReceive(out TransportEvent e);
    }

    public sealed class NetworkConditions
    {
        public int LatencyTicks { get; set; }
        public int JitterTicks { get; set; }
        /// <summary>Probability that an unreliable packet is dropped.</summary>
        public float Loss { get; set; }
        /// <summary>Probability that an unreliable packet is delivered twice.</summary>
        public float Duplicate { get; set; }
    }

    /// <summary>Deterministic in-process network with a virtual clock, used to test multiplayer behaviour headless.</summary>
    public sealed class InMemoryNetwork
    {
        private readonly DeterministicRng rng;
        private readonly Dictionary<long, Endpoint> endpoints = new();
        private readonly List<(long deliverAt, long seq, long to, TransportEvent ev)> inFlight = new();
        private long nextId = 1;
        private long seq;

        public InMemoryNetwork(ulong seed = 1)
        {
            this.rng = new DeterministicRng(seed, 0x4E7);
        }

        public long Now { get; private set; }
        public NetworkConditions Conditions { get; } = new();
        public int DroppedPackets { get; private set; }
        public int DuplicatedPackets { get; private set; }

        public Endpoint CreateEndpoint()
        {
            var ep = new Endpoint(this, this.nextId++);
            this.endpoints[ep.Address] = ep;
            return ep;
        }

        /// <summary>Connect two endpoints; each side sees the other's address as its peer handle.</summary>
        public void Connect(Endpoint a, Endpoint b)
        {
            a.Links.Add(b.Address);
            b.Links.Add(a.Address);
            this.Enqueue(a.Address, new TransportEvent(TransportEventType.Connected, b.Address, null), 0);
            this.Enqueue(b.Address, new TransportEvent(TransportEventType.Connected, a.Address, null), 0);
        }

        /// <summary>Abruptly cut a link (cable pulled / crash): both sides get a Disconnected event, in-flight data is lost.</summary>
        public void Cut(Endpoint a, Endpoint b)
        {
            if (!a.Links.Remove(b.Address))
                return;
            b.Links.Remove(a.Address);
            this.inFlight.RemoveAll(f => (f.to == a.Address && f.ev.Peer == b.Address) || (f.to == b.Address && f.ev.Peer == a.Address));
            this.Enqueue(a.Address, new TransportEvent(TransportEventType.Disconnected, b.Address, null), 0);
            this.Enqueue(b.Address, new TransportEvent(TransportEventType.Disconnected, a.Address, null), 0);
        }

        internal void Close(Endpoint a, Endpoint b)
        {
            if (!a.Links.Remove(b.Address))
                return;
            b.Links.Remove(a.Address);
            long last = this.inFlight.Where(f => f.to == a.Address || f.to == b.Address).Select(f => f.deliverAt).DefaultIfEmpty(this.Now).Max();
            int delay = (int)(last - this.Now) + 1;
            this.Enqueue(a.Address, new TransportEvent(TransportEventType.Disconnected, b.Address, null), delay);
            this.Enqueue(b.Address, new TransportEvent(TransportEventType.Disconnected, a.Address, null), delay);
        }

        /// <summary>Cut silently: no disconnect notification, packets are simply lost (tests timeout detection).</summary>
        public void Blackhole(Endpoint a, Endpoint b)
        {
            a.Links.Remove(b.Address);
            b.Links.Remove(a.Address);
        }

        public void Advance(int ticks = 1)
        {
            this.Now += ticks;
            var due = this.inFlight.Where(f => f.deliverAt <= this.Now).OrderBy(f => f.deliverAt).ThenBy(f => f.seq).ToList();
            foreach (var f in due)
            {
                this.inFlight.Remove(f);
                if (this.endpoints.TryGetValue(f.to, out var ep))
                    ep.Inbox.Enqueue(f.ev);
            }
        }

        private void Enqueue(long to, TransportEvent ev, int delay) => this.inFlight.Add((this.Now + delay, this.seq++, to, ev));

        internal void Send(Endpoint from, long to, byte[] data, bool reliable)
        {
            if (!from.Links.Contains(to))
                return;
            var c = this.Conditions;
            int delay = c.LatencyTicks + (c.JitterTicks > 0 ? this.rng.NextInt(0, c.JitterTicks + 1) : 0);
            if (reliable)
            {
                // reliable channel: ordered, so it may not overtake earlier reliable packets on this link
                long last = this.inFlight.Where(f => f.to == to && f.ev.Peer == from.Address).Select(f => f.deliverAt).DefaultIfEmpty(0).Max();
                long at = Math.Max(this.Now + delay, last);
                this.inFlight.Add((at, this.seq++, to, new TransportEvent(TransportEventType.Data, from.Address, data)));
                return;
            }
            if (this.rng.Chance(c.Loss))
            {
                this.DroppedPackets++;
                return;
            }
            this.Enqueue(to, new TransportEvent(TransportEventType.Data, from.Address, data), delay);
            if (this.rng.Chance(c.Duplicate))
            {
                this.DuplicatedPackets++;
                this.Enqueue(to, new TransportEvent(TransportEventType.Data, from.Address, data), delay + 1);
            }
        }

        public sealed class Endpoint : INetTransport
        {
            private readonly InMemoryNetwork network;

            internal Endpoint(InMemoryNetwork network, long address)
            {
                this.network = network;
                this.Address = address;
            }

            public long Address { get; }
            internal HashSet<long> Links { get; } = new();
            internal Queue<TransportEvent> Inbox { get; } = new();

            public void Send(long peer, byte[] data, bool reliable) => this.network.Send(this, peer, data, reliable);

            /// <summary>Graceful close: data already sent is still delivered, then both sides see Disconnected.</summary>
            public void Disconnect(long peer)
            {
                if (this.network.endpoints.TryGetValue(peer, out var other))
                    this.network.Close(this, other);
            }

            public bool TryReceive(out TransportEvent e) => this.Inbox.TryDequeue(out e);
        }
    }
}
