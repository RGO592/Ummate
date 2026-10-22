using System;
using UnityEngine;

public static class HexMetrics
{
    public const float Sqrt3 = 1.7320508075688772f;
    public static float Radius(float width) => width / Sqrt3;
    public static Vector3 LocalCenter(HexCoord coord, float width) =>
        new Vector3(width * (coord.q + coord.r * 0.5f), 0, Radius(width) * 1.5f * coord.r);
    public static Vector3 HexToWorld(Transform root, HexCoord coord, float width) => root.TransformPoint(LocalCenter(coord, width));
    public static HexCoord WorldToHex(Transform root, Vector3 world, float width) => LocalToHex(root.InverseTransformPoint(world), width);
    public static HexCoord LocalToHex(Vector3 local, float width)
    {
        float radius = Radius(width);
        return CubeRound((Sqrt3 / 3f * local.x - local.z / 3f) / radius, (2f / 3f * local.z) / radius);
    }
    public static HexCoord CubeRound(float q, float r)
    {
        float s = -q - r;
        int rq = Mathf.RoundToInt(q), rr = Mathf.RoundToInt(r), rs = Mathf.RoundToInt(s);
        float dq = Mathf.Abs(rq - q), dr = Mathf.Abs(rr - r), ds = Mathf.Abs(rs - s);
        if (dq > dr && dq > ds) rq = -rr - rs;
        else if (dr > ds) rr = -rq - rs;
        return new HexCoord(rq, rr);
    }
    // Odd rows are shifted east by half a width. Works for negative rows too.
    public static HexCoord FromOffset(int col, int row) => new HexCoord(col - (row - (row & 1)) / 2, row);
    public static int OffsetColumn(HexCoord coord) => coord.q + (coord.r - (coord.r & 1)) / 2;
    public static bool InOffsetRect(HexCoord coord, int columns, int rows)
    {
        int col = OffsetColumn(coord);
        return col >= 0 && col < columns && coord.r >= 0 && coord.r < rows;
    }
    public static Vector3 EdgeNormal(int direction) => LocalCenter(HexCoord.Directions[direction], 1f).normalized;
    // Corner d is the intersection of edges d and d+1. Counter-clockwise in XZ.
    public static Vector3 Corner(int direction, float width) =>
        (EdgeNormal(direction) + EdgeNormal((direction + 1) % 6)) * (width / 3f);
    public static bool Contains(Vector3 local, float width, float inset = 0f)
    {
        for (int d = 0; d < 6; d++)
            if (Vector3.Dot(local, EdgeNormal(d)) > width * 0.5f - inset + 0.00001f) return false;
        return true;
    }
}
