using FlaxEngine;

namespace Reflect;

/// <summary>
/// Project-wide Reflect config, so it does not depend on which scene is loaded.
/// Create a Json asset of this type and add it in Game Settings > Custom Settings under the key "Reflect".
/// </summary>
public sealed class ReflectSettings
{
    public const string Key = "Reflect";

    [Tooltip("Prefabs that can be spawned over the network.")]
    public Prefab[] SpawnablePrefabs = [];

    [Tooltip("Seconds between server state sync ticks.")]
    public float SyncInterval = 0.1f;

    public string Address = "127.0.0.1";
    public ushort Port = 7777;
    public ushort MaxConnections = 16;

    /// <summary>Reads the asset registered under <see cref="Key"/>, or defaults if none is set.</summary>
    public static ReflectSettings Load()
    {
        var asset = Engine.GetCustomSettings(Key);
        if (asset == null || asset.WaitForLoaded())
        {
            Debug.LogWarning($"No '{Key}' entry in Game Settings > Custom Settings, using defaults.");
            return new ReflectSettings();
        }
        return asset.CreateInstance<ReflectSettings>() ?? new ReflectSettings();
    }
}
