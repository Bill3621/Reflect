namespace Reflect;

public enum DisconnectReason : byte
{
    /// <summary>This client called <see cref="NetworkClient.Disconnect()"/>.</summary>
    ClientRequested = 0,
    /// <summary>The connection closed before it was ever established, usually an unreachable server.</summary>
    ConnectFailed = 1,
    /// <summary>The server closed the connection without saying why, for example by shutting down.</summary>
    ServerClosed = 2,
    /// <summary>The connection timed out.</summary>
    ConnectionLost = 3,
    /// <summary>The server kicked this client with <see cref="NetworkServer.Kick"/>.</summary>
    Kicked = 4,
    /// <summary>The server asked for a scene this client could not load.</summary>
    SceneLoadFailed = 5,
}

/// <summary>Passed to disconnect events. <see cref="Message"/> is the server's text for a kick, otherwise empty.</summary>
public readonly record struct DisconnectInfo(DisconnectReason Reason, string Message = "");
