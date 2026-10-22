using System;
using System.Collections.ObjectModel;

// Axial coordinates. This is the sole direction ordering used by meshes, masks and shaders.
[Serializable]
public struct HexCoord : IEquatable<HexCoord>
{
    public int q;
    public int r;
    public int s => -q - r;
    public HexCoord(int q, int r) { this.q = q; this.r = r; }
    public static readonly ReadOnlyCollection<HexCoord> Directions = Array.AsReadOnly(new[]
    {
        new HexCoord(1, 0), new HexCoord(0, 1), new HexCoord(-1, 1),
        new HexCoord(-1, 0), new HexCoord(0, -1), new HexCoord(1, -1)
    });
    public HexCoord Neighbor(int direction) => this + Directions[((direction % 6) + 6) % 6];
    public int Distance(HexCoord other) => Distance(this, other);
    public static int Distance(HexCoord a, HexCoord b) =>
        (Math.Abs(a.q - b.q) + Math.Abs(a.r - b.r) + Math.Abs(a.s - b.s)) / 2;
    // Positive steps rotate E toward NE in the XZ plane; independent of camera yaw.
    public HexCoord Rotate60(int steps = 1)
    {
        HexCoord result = this;
        for (int i = 0, count = ((steps % 6) + 6) % 6; i < count; i++)
            result = new HexCoord(-result.r, -result.s);
        return result;
    }
    public bool Equals(HexCoord other) => q == other.q && r == other.r;
    public override bool Equals(object obj) => obj is HexCoord other && Equals(other);
    public override int GetHashCode() { unchecked { return (q * 397) ^ r; } }
    public static bool operator ==(HexCoord a, HexCoord b) => a.Equals(b);
    public static bool operator !=(HexCoord a, HexCoord b) => !a.Equals(b);
    public static HexCoord operator +(HexCoord a, HexCoord b) => new HexCoord(a.q + b.q, a.r + b.r);
    public override string ToString() => $"({q}, {r})";
}

[Flags]
public enum HexDirectionMask
{
    None = 0, E = 1 << 0, NE = 1 << 1, NW = 1 << 2,
    W = 1 << 3, SW = 1 << 4, SE = 1 << 5, All = 63
}
