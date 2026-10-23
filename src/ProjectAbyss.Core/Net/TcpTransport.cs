using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ProjectAbyss.Core.Net
{
    /// <summary>
    /// Direct TCP transport (length-prefixed frames). Everything is delivered reliably and in order; the
    /// "unreliable" flag is accepted for interface compatibility. Used for LAN/direct-IP play and integration tests.
    /// No data leaves the machine except to the peer the player chose to connect to.
    /// </summary>
    public sealed class TcpTransport : INetTransport, IDisposable
    {
        public const int MaxFrame = 8 * 1024 * 1024;
        private readonly ConcurrentQueue<TransportEvent> inbox = new();
        private readonly ConcurrentDictionary<long, Connection> connections = new();
        private TcpListener? listener;
        private long nextHandle = 1;
        private volatile bool disposed;

        private sealed class Connection
        {
            public TcpClient Client = null!;
            public NetworkStream Stream = null!;
            public readonly object WriteLock = new();
        }

        public int ListenPort => ((IPEndPoint?)this.listener?.LocalEndpoint)?.Port ?? 0;

        public static TcpTransport Listen(IPAddress address, int port)
        {
            var t = new TcpTransport { listener = new TcpListener(address, port) };
            t.listener.Start();
            var thread = new Thread(t.AcceptLoop) { IsBackground = true, Name = "abyss-tcp-accept" };
            thread.Start();
            return t;
        }

        public static TcpTransport Connect(string host, int port)
        {
            var t = new TcpTransport();
            var client = new TcpClient { NoDelay = true };
            client.Connect(host, port);
            t.Attach(client);
            return t;
        }

        private void AcceptLoop()
        {
            while (!this.disposed)
            {
                try
                {
                    var client = this.listener!.AcceptTcpClient();
                    client.NoDelay = true;
                    this.Attach(client);
                }
                catch (SocketException)
                {
                    if (this.disposed)
                        return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        private void Attach(TcpClient client)
        {
            long handle = Interlocked.Increment(ref this.nextHandle);
            var conn = new Connection { Client = client, Stream = client.GetStream() };
            this.connections[handle] = conn;
            this.inbox.Enqueue(new TransportEvent(TransportEventType.Connected, handle, null));
            var thread = new Thread(() => this.ReadLoop(handle, conn)) { IsBackground = true, Name = "abyss-tcp-read" };
            thread.Start();
        }

        private void ReadLoop(long handle, Connection conn)
        {
            var header = new byte[4];
            try
            {
                while (!this.disposed)
                {
                    ReadExactly(conn.Stream, header);
                    int len = BitConverter.ToInt32(header, 0);
                    if (len <= 0 || len > MaxFrame)
                        throw new IOException("invalid frame length");
                    var data = new byte[len];
                    ReadExactly(conn.Stream, data);
                    this.inbox.Enqueue(new TransportEvent(TransportEventType.Data, handle, data));
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
            }
            this.Close(handle);
        }

        private static void ReadExactly(Stream s, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = s.Read(buffer, read, buffer.Length - read);
                if (n <= 0)
                    throw new IOException("connection closed");
                read += n;
            }
        }

        public void Send(long peer, byte[] data, bool reliable)
        {
            if (!this.connections.TryGetValue(peer, out var conn))
                return;
            try
            {
                lock (conn.WriteLock)
                {
                    conn.Stream.Write(BitConverter.GetBytes(data.Length), 0, 4);
                    conn.Stream.Write(data, 0, data.Length);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
                this.Close(peer);
            }
        }

        public void Disconnect(long peer)
        {
            if (this.connections.TryGetValue(peer, out var conn))
            {
                try
                {
                    lock (conn.WriteLock)
                        conn.Stream.Flush();
                    conn.Client.Client.Shutdown(SocketShutdown.Send);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
                {
                }
            }
            this.Close(peer);
        }

        private void Close(long handle)
        {
            if (!this.connections.TryRemove(handle, out var conn))
                return;
            conn.Client.Close();
            this.inbox.Enqueue(new TransportEvent(TransportEventType.Disconnected, handle, null));
        }

        public bool TryReceive(out TransportEvent e) => this.inbox.TryDequeue(out e);

        public void Dispose()
        {
            this.disposed = true;
            this.listener?.Stop();
            foreach (var h in this.connections.Keys)
                this.Close(h);
        }
    }
}
