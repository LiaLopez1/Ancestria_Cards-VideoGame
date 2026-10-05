/// <summary>
/// Identidad de los bots dentro de la arquitectura de red.
///
/// Un bot no tiene un clientId real de Netcode, así que usa ids reservados
/// en el extremo alto de ulong - igual que el boss usa BOSS_ID
/// (ulong.MaxValue) dentro de DeckManager:
///   slot 1 -> ulong.MaxValue - 1
///   slot 2 -> ulong.MaxValue - 2
/// El slot 0 siempre es el jugador humano (host) en modo local.
///
/// OJO: estas funciones solo tienen sentido si NetworkBootstrap.ModoLocalConBots
/// es true. En una partida online los slots 1 y 2 son jugadores reales.
/// </summary>
public static class BotIds
{
    public const int Cantidad = 2;
    public const int PrimerSlot = 1;

    public static bool EsSlotDeBot(int slot)
    {
        return slot >= PrimerSlot && slot < PrimerSlot + Cantidad;
    }

    public static ulong IdDeSlot(int slot)
    {
        return ulong.MaxValue - (ulong)slot;
    }

    public static bool EsBot(ulong id)
    {
        return id < ulong.MaxValue && id >= ulong.MaxValue - (ulong)Cantidad;
    }

    public static bool TryObtenerSlot(ulong id, out int slot)
    {
        if (EsBot(id))
        {
            slot = (int)(ulong.MaxValue - id);
            return true;
        }

        slot = -1;
        return false;
    }
}