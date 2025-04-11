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
        public const int GameFPS = 30;
    }
}