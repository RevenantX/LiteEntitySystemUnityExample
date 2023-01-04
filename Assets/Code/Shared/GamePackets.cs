using System;
using UnityEngine;

namespace Code.Shared
{
    public enum PacketType : byte
    {
        EntitySystem,
        Serialized
    }
    
    //Auto serializable packets
    public class JoinPacket
    {
        public string UserName { get; set; }
    }

    [Flags]
    public enum MovementKeys : byte
    {
        Left = 1,
        Right = 1 << 1,
        Up = 1 << 2,
        Down = 1 << 3,
        Fire = 1 << 4,
        Projectile = 1 << 5
    }

    public struct ShootPacket
    {
        public Vector2 Origin;
        public Vector2 Hit;
    }
    
    public struct PlayerInputPacket
    {
        public MovementKeys Keys;
        public float Rotation;
    }
}