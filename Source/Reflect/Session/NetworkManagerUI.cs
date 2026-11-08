using System;
using FlaxEngine;

namespace Reflect;

/// <summary>
/// Put in the scene a process starts in. Starts the command-line role shortly after play begins and exposes
/// inspector buttons. Config lives in the <see cref="ReflectSettings"/> asset, not here.
/// </summary>
public class NetworkManagerUI : Script
{
    [Header("States")]
    [ShowInEditor] public bool Initialized => NetworkManager.Instance != null;
    [ShowInEditor] public bool IsServer => NetworkManager.IsServer;
    [ShowInEditor] public bool IsClient => NetworkManager.IsClient;

    private float _startTimer;
    private const float TimeBeforeStart = 0.25f;
    public override void OnUpdate()
    {
        if (_startTimer >= TimeBeforeStart) return;
        _startTimer += Time.DeltaTime;
        if (_startTimer < TimeBeforeStart) return;
        NetworkManager.Instance?.OnStart();
    }

    [Button]
    public void StartServer() => NetworkManager.Instance.StartServer();

    [Button]
    public void StartClient() => NetworkManager.Instance.StartClient();

    [Button]
    public void StopServer() => NetworkManager.Instance.Server?.Stop();
    [Button]
    public void StopClient() => NetworkManager.Instance.Client?.Disconnect();
}
