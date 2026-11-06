using FlaxEngine;

namespace Reflect;

// Restore the level's scene actors when a network session ends.
internal readonly struct SceneObjectState(NetworkIdentity identity)
{
    private readonly NetworkIdentity _identity = identity;
    private readonly Vector3 _position = identity.Actor.Position;
    private readonly Quaternion _orientation = identity.Actor.Orientation;
    private readonly bool _isActive = identity.Actor.IsActive;

    public void Restore()
    {
        if (!_identity || !_identity.Actor) return;
        _identity.ResetNetworkState();
        _identity.Actor.Position = _position;
        _identity.Actor.Orientation = _orientation;
        _identity.Actor.IsActive = _isActive;
    }
}
