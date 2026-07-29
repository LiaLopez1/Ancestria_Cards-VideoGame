using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Vive en el GameScene. Conecta los dos botones que aparecen en los
/// paneles de resultado (victoria, derrota por boss, derrota por sospecha):
///
///   - "Volver a jugar" (SolicitarVolverAJugar): cualquier jugador lo puede
///     apretar - se manda al servidor, que reinicia TODO para una ronda
///     nueva SIN salir de la sala (mazo, manos, turno, jefe, sospecha,
///     resultado). Solo funciona si la partida realmente ya termino.
///
///   - "Volver al menu" (VolverAlMenu): puramente local, sin RPC. Desconecta
///     de Netcode, sale de la sala de PlayFab, y carga la escena del menu.
///     Si lo aprieta el host, esto efectivamente cierra la sala para todos
///     (al caerse el host, Netcode desconecta al resto).
/// </summary>
public class GameRestartManager : NetworkBehaviour
{
    [Header("Referencias (servidor)")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private DeckManager deckManager;
    [SerializeField] private SuspicionManager suspicionManager;

    [Header("Escena del menu principal")]
    [Tooltip("Nombre exacto de la escena a la que se vuelve - debe coincidir con la escena donde vive StartupFlowUI.")]
    [SerializeField] private string nombreEscenaMenu = "MainMenu";

    /// <summary>Conectar al boton "Volver a jugar" de los 3 paneles de resultado.</summary>
    public void SolicitarVolverAJugar()
    {
        SolicitarVolverAJugarServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void SolicitarVolverAJugarServerRpc(ServerRpcParams rpcParams = default)
    {
        if (gameManager == null || !gameManager.PartidaTerminada)
        {
            Debug.LogWarning("[GameRestartManager] Se pidio reiniciar, pero la partida todavia no termino - se ignora.");
            return;
        }

        Debug.Log($"[Servidor] Cliente {rpcParams.Receive.SenderClientId} pidio reiniciar la partida.");

        if (deckManager != null)
        {
            deckManager.ReiniciarPartida();
        }

        if (suspicionManager != null)
        {
            suspicionManager.ReiniciarSospecha();
        }
    }

    /// <summary>
    /// Conectar al boton "Volver al menu" de los 3 paneles de resultado.
    /// Puramente local - cada cliente lo ejecuta sobre si mismo, sin
    /// necesitar permiso del servidor (irse de la sala es una decision
    /// personal, no algo que haya que validar).
    /// </summary>
    public void VolverAlMenu()
    {
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.SalirDeSalaActual();
        }

        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
        {
            NetworkManager.Singleton.Shutdown();
        }

        SceneManager.LoadScene(nombreEscenaMenu);
    }
}