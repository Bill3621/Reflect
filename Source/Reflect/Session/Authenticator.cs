namespace Reflect;

public interface IServerAuthenticator
{
    void OnPayload(NetworkServer server, NetworkConnection conn, NetworkReader payload);

    void OnDisconnected(NetworkConnection conn);
}

public interface IClientAuthenticator
{
    void WritePayload(NetworkWriter writer);

    void OnDisconnected();
}
