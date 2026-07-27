using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Maneja las trampas del juego. Por ahora solo la primera: "solicitar carta
/// por categoria" - el jugador elige una categoria, el servidor valida quien
/// la pidio, y le avisa a TODOS (es una declaracion publica, todos deben ver
/// el icono junto al nombre de quien pidio).
///
/// Esto es solo la DECLARACION/visual de la trampa por ahora - todavia no
/// intercambia cartas entre jugadores, eso lo definimos despues.
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

    /// <summary>Llamado por CategoryRequestPanelUI cuando el jugador elige una categoria.</summary>
    public void SolicitarCategoria(CardCategory categoria)
    {
        SolicitarCategoriaServerRpc(categoria);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SolicitarCategoriaServerRpc(CardCategory categoria, ServerRpcParams rpcParams = default)
    {
        ulong clienteSolicitante = rpcParams.Receive.SenderClientId;

        if (!NetworkBootstrap.Instance.TryObtenerSlot(clienteSolicitante, out int slot))
        {
            Debug.LogWarning($"[Servidor] No se encontró el slot del cliente {clienteSolicitante} al pedir categoría.");
            return;
        }

        Debug.Log($"[Servidor] Slot {slot} solicitó la categoría {categoria}.");

        MostrarSolicitudClientRpc(slot, categoria);
    }

    /// <summary>Se ejecuta en TODOS los clientes: la solicitud es pública.</summary>
    [ClientRpc]
    private void MostrarSolicitudClientRpc(int slot, CardCategory categoria)
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

        TrapNotificationUI.Instance?.MostrarNotificacion(nombreJugador, categoria.ToString(), icono);
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