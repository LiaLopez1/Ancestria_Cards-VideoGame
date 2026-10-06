using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Maneja las trampas del juego. Por ahora dos variantes que comparten el
/// mismo panel de 4 categorias (CategoryRequestPanelUI decide cual se usa
/// segun que boton abrio el panel):
///   - "Necesito categoria X" (SolicitarCategoria)
///   - "Tengo categoria X" (MostrarCategoria)
/// En ambos casos el servidor valida quien la pidio y le avisa a TODOS (es
/// una declaracion publica, todos deben ver el icono junto al nombre).
///
/// Esto es solo la DECLARACION/visual de la trampa por ahora - todavia no
/// intercambia cartas entre jugadores, eso lo definimos despues.
///
/// CAMBIOS PARA BOTS:
///  - OnTrampaDeclarada: evento SOLO del servidor, se dispara por cada
///    declaracion (de un humano o de un bot) para que los bots aprendan
///    de la informacion publica.
///  - DeclararComoBot(): entrada directa por slot para que un bot declare,
///    sin pasar por ServerRpc (un bot no tiene cliente real).
/// </summary>
public class TrapManager : NetworkBehaviour
{
    [System.Serializable]
    public class IconoPorCategoria
    {
        public CardCategory categoria;
        public Sprite icono;
    }

    [Header("Iconos por categoria")]
    [SerializeField] private IconoPorCategoria[] iconosPorCategoria;

    /// <summary>
    /// SOLO servidor. (slot, categoria, tieneCategoria): true = "tengo"
    /// (mostrar), false = "necesito" (pedir).
    /// </summary>
    public event System.Action<int, CardCategory, bool> OnTrampaDeclarada;

    /// <summary>
    /// Llamado por CategoryRequestPanelUI cuando el jugador elige una
    /// categoria en modo "Pedir" (necesita esa categoria).
    /// </summary>
    public void SolicitarCategoria(CardCategory categoria)
    {
        SolicitarCategoriaServerRpc(categoria, tieneCategoria: false);
    }

    /// <summary>
    /// Llamado por CategoryRequestPanelUI cuando el jugador elige una
    /// categoria en modo "Mostrar" (declara que tiene esa categoria).
    /// </summary>
    public void MostrarCategoria(CardCategory categoria)
    {
        SolicitarCategoriaServerRpc(categoria, tieneCategoria: true);
    }

    /// <summary>
    /// SOLO servidor (BotController). Un bot declara una categoria. No
    /// valida turno: igual que con los humanos, pedir/mostrar es libre.
    /// </summary>
    public void DeclararComoBot(int slot, CardCategory categoria, bool tieneCategoria)
    {
        if (!IsServer)
        {
            return;
        }

        string verbo = tieneCategoria ? "tiene" : "solicitó";
        Debug.Log($"[Servidor] Bot del slot {slot} {verbo} la categoría {categoria}.");

        PublicarDeclaracion(slot, categoria, tieneCategoria);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SolicitarCategoriaServerRpc(CardCategory categoria, bool tieneCategoria, ServerRpcParams rpcParams = default)
    {
        ulong clienteSolicitante = rpcParams.Receive.SenderClientId;

        if (!NetworkBootstrap.Instance.TryObtenerSlot(clienteSolicitante, out int slot))
        {
            Debug.LogWarning($"[Servidor] No se encontró el slot del cliente {clienteSolicitante} al pedir categoría.");
            return;
        }

        string verbo = tieneCategoria ? "tiene" : "solicitó";
        Debug.Log($"[Servidor] Slot {slot} {verbo} la categoría {categoria}.");

        PublicarDeclaracion(slot, categoria, tieneCategoria);
    }

    /// <summary>SOLO servidor. Avisa a todos los clientes (visual) y a los suscriptores locales (bots).</summary>
    private void PublicarDeclaracion(int slot, CardCategory categoria, bool tieneCategoria)
    {
        MostrarSolicitudClientRpc(slot, categoria, tieneCategoria);
        OnTrampaDeclarada?.Invoke(slot, categoria, tieneCategoria);
    }

    /// <summary>Se ejecuta en TODOS los clientes: la solicitud es pública.</summary>
    [ClientRpc]
    private void MostrarSolicitudClientRpc(int slot, CardCategory categoria, bool tieneCategoria)
    {
        Sprite icono = ObtenerIconoDeCategoria(categoria);

        if (icono == null)
        {
            Debug.LogWarning($"[Cliente] No hay icono configurado para la categoría {categoria}.");
            return;
        }

        string nombreJugador = PlayerNamePanelsUI.Instance != null
            ? PlayerNamePanelsUI.Instance.ObtenerNombrePorSlot(slot)
            : $"Jugador {slot}";

        TrapNotificationUI.Instance?.MostrarNotificacion(nombreJugador, categoria.ToString(), icono, tieneCategoria);
    }

    private Sprite ObtenerIconoDeCategoria(CardCategory categoria)
    {
        foreach (IconoPorCategoria entrada in iconosPorCategoria)
        {
            if (entrada.categoria == categoria)
            {
                return entrada.icono;
            }
        }
        return null;
    }
}