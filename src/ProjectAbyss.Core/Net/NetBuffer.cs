using System;
using System.IO;
using System.Text;

namespace ProjectAbyss.Core.Net
{
    /// <summary>Thrown for any malformed or out-of-range network data; the sender is disconnected.</summary>
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
    }

    public sealed class NetWriter
    {
        private readonly MemoryStream stream = new();
        private readonly BinaryWriter writer;

        public NetWriter()
        {
            this.writer = new BinaryWriter(this.stream, Encoding.UTF8, leaveOpen: true);
        }

        public NetWriter U8(byte v) { this.writer.Write(v); return this; }
        public NetWriter Bool(bool v) { this.writer.Write(v); return this; }
        public NetWriter I16(short v) { this.writer.Write(v); return this; }
        public NetWriter U16(ushort v) { this.writer.Write(v); return this; }
        public NetWriter I32(int v) { this.writer.Write(v); return this; }
        public NetWriter U32(uint v) { this.writer.Write(v); return this; }
        public NetWriter I64(long v) { this.writer.Write(v); return this; }
        public NetWriter F32(float v) { this.writer.Write(v); return this; }

        public NetWriter Str(string? v)
        {
            v ??= "";
            byte[] b = Encoding.UTF8.GetBytes(v);
            if (b.Length > ushort.MaxValue)
                throw new ArgumentException("string too long");
            this.writer.Write((ushort)b.Length);
            this.writer.Write(b);
            return this;
        }

        public NetWriter Bytes(byte[] v)
        {
            this.writer.Write(v.Length);
            this.writer.Write(v);
            return this;
        }

        /// <summary>World position quantised to 1/8 pixel.</summary>
        public NetWriter Pos(float v) => this.I32((int)MathF.Round(v * 8f));

        public byte[] ToArray()
        {
            this.writer.Flush();
            return this.stream.ToArray();
        }
    }

    public sealed class NetReader
    {
        public const int MaxBytesField = 4 * 1024 * 1024;
        private readonly BinaryReader reader;
        private readonly long length;

        public NetReader(byte[] data)
        {
            this.length = data.Length;
            this.reader = new BinaryReader(new MemoryStream(data, writable: false), Encoding.UTF8);
        }

        private void Need(int n)
        {
            if (this.reader.BaseStream.Position + n > this.length)
                throw new ProtocolException("message truncated");
        }

        public byte U8() { this.Need(1); return this.reader.ReadByte(); }
        public bool Bool() { this.Need(1); return this.reader.ReadBoolean(); }
        public short I16() { this.Need(2); return this.reader.ReadInt16(); }
        public ushort U16() { this.Need(2); return this.reader.ReadUInt16(); }
        public int I32() { this.Need(4); return this.reader.ReadInt32(); }
        public uint U32() { this.Need(4); return this.reader.ReadUInt32(); }
        public long I64() { this.Need(8); return this.reader.ReadInt64(); }

        public float F32()
        {
            this.Need(4);
            float v = this.reader.ReadSingle();
            if (float.IsNaN(v) || float.IsInfinity(v))
                throw new ProtocolException("invalid float");
            return v;
        }

        public string Str(int maxLength = 256)
        {
            int len = this.U16();
            if (len > maxLength * 4)
                throw new ProtocolException("string too long");
            this.Need(len);
            string s = Encoding.UTF8.GetString(this.reader.ReadBytes(len));
            if (s.Length > maxLength)
                throw new ProtocolException("string too long");
            return s;
        }

        public byte[] Bytes(int maxLength = MaxBytesField)
        {
            int len = this.I32();
            if (len < 0 || len > maxLength)
                throw new ProtocolException("byte field too long");
            this.Need(len);
            return this.reader.ReadBytes(len);
        }

        public float Pos() => this.I32() / 8f;

        public int Count(int max)
        {
            int n = this.U16();
            if (n > max)
                throw new ProtocolException($"count {n} exceeds {max}");
            return n;
        }

        public bool AtEnd => this.reader.BaseStream.Position >= this.length;
    }
}
