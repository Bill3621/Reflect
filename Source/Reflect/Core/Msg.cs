using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Reflect;

public enum MsgType : byte
{
    Spawn = 1,
    Despawn = 2,
    State = 3,
    WelcomeDone = 4,
    Command = 5,
    ClientRpc = 6,
    TargetRpc = 7
}

public static class Msg
{
    public static void WriteHeader(NetworkWriter w, MsgType t) => w.Write((byte)t);
    public static MsgType ReadHeader(NetworkReader r) => (MsgType)r.ReadByte();
}
