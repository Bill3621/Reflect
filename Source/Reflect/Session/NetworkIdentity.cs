using System;
using FlaxEngine;

namespace Reflect;

/// <summary>
/// Attach to the root Actor of any networked object.
/// </summary>
public class NetworkIdentity : Script
{

    [ShowInEditor]
    [ReadOnly]
    public uint NetId { get; internal set; }

    public ulong SceneId;

    [ReadOnly]
    public Guid AssetId;

    public static bool IsServer => NetworkManager.Instance && NetworkManager.Instance.IsServer;
    public static bool IsClient => NetworkManager.Instance && NetworkManager.Instance.IsClient;

    public NetworkConnection Owner { get; internal set; }

    public bool IsOwnedLocally { get; internal set; }

    private NetworkScript[] _scripts;
    public NetworkScript[] Scripts
    {
        get
        {
            if (_scripts != null) return _scripts;
            
            _scripts = Actor.GetScripts<NetworkScript>();
            for (byte i = 0; i < _scripts.Length; i++)
            {
                _scripts[i].Identity = this;
                _scripts[i].ComponentIndex = i;
            }
            return _scripts;
        }
    }

    public bool AnyDirty()
    {
        var s = Scripts;
        for(int i = 0; i < s.Length; i++)
            if (s[i].AnyDirty())
                return true;
        return false;
    }
}
