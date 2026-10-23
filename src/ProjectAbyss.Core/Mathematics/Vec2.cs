using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectAbyss.Core.Mathematics
{
    /// <summary>Engine-independent 2D vector in world pixels (1 tile = <see cref="WorldUnits.TileSize"/> pixels).</summary>
    [JsonConverter(typeof(Vec2JsonConverter))]
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;

        public static readonly Vec2 Zero = new(0, 0);

        public Vec2(float x, float y)
        {
            this.X = x;
            this.Y = y;
        }

        public float Length => MathF.Sqrt(this.X * this.X + this.Y * this.Y);
        public float LengthSquared => this.X * this.X + this.Y * this.Y;

        public Vec2 Normalized()
        {
            float len = this.Length;
            return len < 1e-6f ? Zero : new Vec2(this.X / len, this.Y / len);
        }

        /// <summary>Angle in radians, 0 = right (+X), counter-clockwise towards -Y (screen up).</summary>
        public float Angle => MathF.Atan2(-this.Y, this.X);

        public static Vec2 FromAngle(float radians) => new(MathF.Cos(radians), -MathF.Sin(radians));

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static float DistanceSquared(Vec2 a, Vec2 b) => (a - b).LengthSquared;

        public Vec2 ClampLength(float max)
        {
            float lenSq = this.LengthSquared;
            if (lenSq <= max * max)
                return this;
            return this.Normalized() * max;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float s) => new(a.X * s, a.Y * s);
        public static Vec2 operator *(float s, Vec2 a) => new(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, float s) => new(a.X / s, a.Y / s);
        public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
        public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);

        public bool Equals(Vec2 other) => this.X.Equals(other.X) && this.Y.Equals(other.Y);
        public override bool Equals(object? obj) => obj is Vec2 other && this.Equals(other);
        public override int GetHashCode() => HashCode.Combine(this.X, this.Y);
        public override string ToString() => $"({this.X:0.##}, {this.Y:0.##})";
    }

    /// <summary>Serialises <see cref="Vec2"/> as a compact [x, y] array.</summary>
    public sealed class Vec2JsonConverter : JsonConverter<Vec2>
    {
        public override Vec2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("Expected [x, y] for Vec2.");
            reader.Read();
            float x = reader.GetSingle();
            reader.Read();
            float y = reader.GetSingle();
            reader.Read();
            if (reader.TokenType != JsonTokenType.EndArray)
                throw new JsonException("Expected end of Vec2 array.");
            return new Vec2(x, y);
        }

        public override void Write(Utf8JsonWriter writer, Vec2 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteEndArray();
        }
    }

    public static class WorldUnits
    {
        /// <summary>Base tile size in source pixels (art is authored at 16×16 tiles).</summary>
        public const int TileSize = 16;
    }
}
