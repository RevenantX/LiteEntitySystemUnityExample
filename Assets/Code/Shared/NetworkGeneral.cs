using System;

namespace Code.Shared
{
    public enum GameEntities : ushort
    {
        Player,
        PlayerController,
        GameWeapon,
        WeaponItem,
        BotController,
        Physics,
        Projectile
    }
    
    public static class NetworkGeneral
    {
        public const int ProtocolId = 1;
        public const int GameFPS = 30;
        public static readonly int PacketTypesCount = Enum.GetValues(typeof(PacketType)).Length;

        public const int MaxGameSequence = 1024;
        public const int HalfMaxGameSequence = MaxGameSequence / 2;

        public static int SeqDiff(int a, int b)
        {
            return Diff(a, b, HalfMaxGameSequence);
        }
        public static int Diff(int a, int b, int halfMax)
        {
            return (a - b + halfMax*3) % (halfMax*2) - halfMax;
        }
    }
}