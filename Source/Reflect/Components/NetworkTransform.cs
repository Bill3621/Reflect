using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.Networking;

namespace Reflect;

public class NetworkTransform : NetworkScript
{
   [Header("Sync")]
   [Tooltip("How many transform updates to send per second.")]
   public float SendRate = 20f;

   [Tooltip("Render this many seconds in the past for smooth interpolation.")]
   public float InterpolationDelay = 0.1f;

   [Tooltip("Don't send unless moved/rotated more than these thresholds.")]
   public float PositionThreshold = 0.01f;
   public float RotationThreshold = 0.1f;

   [Header("Authority")]
   [Tooltip("Owning client controls the transform (client-authoritative).")]
   public bool ClientAuthority = true;
   
   private struct Snapshot
   {
      public double Time;
      public Float3 Position;
      public Quaternion Rotation;
   }

   private readonly List<Snapshot> _buffer = [];
   private float _sendAccum;
   private Float3 _lastSentPos;
   private Quaternion _lastSendRot;

   private bool IsLocalOwner => ClientAuthority && IsClient && Identity != null && Identity.IsOwnedLocally;

   public override void OnNetworkSpawn()
   {
      _lastSentPos = Actor.Position;
      _lastSendRot = Actor.Orientation;
   }

   public override void OnUpdate()
   {
      if (IsLocalOwner || (!ClientAuthority && IsServer))
      {
         _sendAccum += Time.DeltaTime;
         if (_sendAccum < 1f / SendRate) return;
         _sendAccum = 0;

         Float3 pos = Actor.Position;
         var rot = Actor.Orientation;

         var moved = Float3.Distance(pos, _lastSentPos) > PositionThreshold;
         var rotated = Quaternion.AngleBetween(rot, _lastSendRot) > RotationThreshold;
         if(!moved && !rotated) return;
         
         _lastSentPos = pos;
         _lastSendRot = rot;

         if (ClientAuthority && IsClient)
         {
            SendCommand(nameof(CmdMove), pos, rot);
         } 
         else if (IsServer)
         {
            SendClientRpc(nameof(RpcMove), pos, rot, false);
         }

         return;
      }
      
      if (_buffer.Count == 0) return;
      var renderTime = Time.GameTime - InterpolationDelay;
      
      if (_buffer.Count == 1 || renderTime >= _buffer[^1].Time)
      {
         var last = _buffer[^1];
         Actor.Position = last.Position;
         Actor.Orientation = last.Rotation;
         return;
      }

      for (var i = 0; i < _buffer.Count - 1; i++)
      {
         var a = _buffer[i];
         var b = _buffer[i + 1];
         if (!(renderTime >= a.Time) || !(renderTime <= b.Time)) continue;
         var t = (float)((renderTime - a.Time) / (b.Time - a.Time));
         Actor.Position = Float3.Lerp(a.Position, b.Position, t);
         Actor.Orientation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
            
         if(i > 0) _buffer.RemoveRange(0, i);
         return;
      }
   }

   public void ServerOverridePosition(Float3 pos)
   {
      _lastSentPos = pos;
      _lastSendRot = Actor.Orientation;
      Actor.Position = pos;
      SendClientRpc(nameof(RpcMove), pos, Actor.Orientation, true);
   }

   [Command(ChannelType = NetworkChannelType.Unreliable)]
   private void CmdMove(Float3 pos, Quaternion rot)
   {
      // TODO: Server validation?
      Actor.Position = pos;
      Actor.Orientation = rot;
      
      SendClientRpc(nameof(RpcMove), pos, rot, false);
   }

   [ClientRpc(ChannelType = NetworkChannelType.Unreliable)]
   private void RpcMove(Float3 pos, Quaternion rot, bool teleport)
   {
      if (teleport)
      {
         _buffer.Clear();
         _lastSentPos = pos;
         _lastSendRot = Actor.Orientation;
         Actor.Position = pos;
         Actor.Orientation = rot;
         return;
      }
      
      if (IsLocalOwner || (!ClientAuthority && IsServer)) return;

      if (_buffer.Count == 0 || Time.GameTime - _buffer[^1].Time > InterpolationDelay * 2f)
      {
         _buffer.Clear();
         _buffer.Add(new Snapshot
         {
            Time = Time.GameTime - InterpolationDelay,
            Position = Actor.Position,
            Rotation = Actor.Orientation,
         });
      }
      
      _buffer.Add(new Snapshot
      {
         Time = Time.GameTime,
         Position =  pos,
         Rotation = rot,
      });
      
      if(_buffer.Count > 32)
         _buffer.RemoveRange(0, _buffer.Count - 32);
   }
}
