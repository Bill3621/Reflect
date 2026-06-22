using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;
using FlaxEngine.Utilities;

namespace Reflect;

public interface IInterestManagement
{
    /// <summary>
    /// Refresh internal acceleration structures for this rebuild.
    /// </summary>
    void Rebuild(IReadOnlyCollection<NetworkIdentity> all) {}
    
    /// <summary>
    /// Fill <paramref name="result"/> with the netIds this connection should
    /// observe. This is the fast path — only touch nearby objects.
    /// </summary>
    void GatherVisible(NetworkConnection conn, HashSet<uint> result) {}
}

/// <summary>
/// Everyone sees everything.
/// </summary>
public sealed class GlobalInterest : IInterestManagement
{
    private readonly HashSet<uint> _allIds = [];

    public void Rebuild(IReadOnlyCollection<NetworkIdentity> all)
    {
        _allIds.Clear();
        _allIds.AddRange(all.Select(x => x.NetId));
    }

    public void GatherVisible(NetworkConnection conn, HashSet<uint> result) => result.AddRange(_allIds);
}

public sealed class DistanceInterest : IInterestManagement
{
    private const float Range = 5000f;

    private readonly Dictionary<uint, Vector3> _allPositions = [];

    public void Rebuild(IReadOnlyCollection<NetworkIdentity> all)
    {
        _allPositions.Clear();
        _allPositions.AddRange(all.Select(x => new KeyValuePair<uint, Vector3>(x.NetId, x.Actor.Position)));
    }

    public void GatherVisible(NetworkConnection conn, HashSet<uint> result)
    {
        var character = conn.OwnedPlayer;
        if (character == null) return;
        result.Add(character.NetId);
        
        const float sqr = Range * Range;
        foreach (var p in _allPositions.Where(pair => Vector3.DistanceSquared(pair.Value, character.Actor.Position) <= sqr))
            result.Add(p.Key);
    }

}

public sealed class GridInterest : IInterestManagement
{
    private const float CellSize = 1000f;
    private const int ViewRadiusInCells = 2;
    private const int HysteresisCells = 1;

    private readonly Dictionary<long, List<NetworkIdentity>> _cells = [];

    private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    private static (int, int) CellOf(Vector3 p) => ((int)Mathf.Floor(p.X / CellSize), (int)Mathf.Floor(p.Z / CellSize));

    public void Rebuild(IReadOnlyCollection<NetworkIdentity> all)
    {
        foreach(var list in _cells.Values) list.Clear();
        
        foreach (var ni in all)
        {
            var (cx, cz) = CellOf(ni.Actor.Position);
            var k = Key(cx, cz);
            if (!_cells.TryGetValue(k, out var list))
                _cells[k] = list = [];
            list.Add(ni);
        }
    }

    public void GatherVisible(NetworkConnection conn, HashSet<uint> result)
    {
        var view = conn.OwnedPlayer;
        if (view == null)
        {
            if (conn.OwnedPlayer != null)
                result.Add(conn.OwnedPlayer.NetId);
            return;
        }

        const int loose = ViewRadiusInCells + HysteresisCells;

        var (vx, vz) = CellOf(view.Actor.Position);
        
        for(var dx = -loose; dx <= loose; dx++)
        for (var dz = -loose; dz <= loose; dz++)
        {
            var k = Key(vx + dx, vz + dz);
            if (!_cells.TryGetValue(k, out var list)) continue;

            var dist = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
            var inTight = dist <= ViewRadiusInCells;

            foreach (var ni in list.Where(ni => inTight || conn.Observing.Contains(ni.NetId)))
                result.Add(ni.NetId);
        }

        result.Add(view.NetId);
    }

}