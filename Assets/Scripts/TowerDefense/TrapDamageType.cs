/// <summary>Type of trap-sourced damage; future resistances can branch on this without changing health APIs.</summary>
public enum TrapDamageType
{
    Physical = 0,  // spikes and bullets
    Fire = 1,      // direct flame and burning
    Explosive = 2, // missile impact and blast
    Energy = 3     // reserved for electrical/energy traps
}
