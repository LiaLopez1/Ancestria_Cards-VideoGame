using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Vive en el GameScene. Conecta los dos botones que aparecen en los
/// paneles de resultado (victoria, derrota por boss, derrota por sospecha):
///
///   - "Volver a jugar" (SolicitarVolverAJugar): es INDIVIDUAL, puramente
///     local - NO manda nada al servidor ni fuerza un reinicio global. Cada
///     jugador que lo aprieta simplemente oculta su propio panel de
///     resultado y ve "Esperando jugadores..." mientras tanto. Si quien lo
///     aprieta es el HOST, ademas se le reactiva su boton "Iniciar partida"
///     - el reinicio real de verdad (mazo, manos, turno, jefe, sospecha)
///     solo ocurre cuando el host lo aprieta a mano de nuevo
///     (DeckManager.OnBotonIniciarPartidaPressed), nunca automaticamente.
///     Una vez que eso pasa, el estado sincronizado (turno, regla, cartas)
///     cambia para TODOS, y ahi cada cliente sale solo de su "esperando".
///
///   - "Volver al menu" (VolverAlMenu): puramente local, sin RPC. Desconecta
///     de Netcode, sale de la sala de PlayFab, y carga la escena del menu.
///     Si lo aprieta el host, esto efectivamente cierra la sala para todos
///     (al caerse el host, Netcode desconecta al resto).
/// </summary>
public class GameRestartManager : NetworkBehaviour
{
    [Header("Referencias (locales, de este cliente)")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private DeckManager deckManager;

    [Header("Escena del menu principal")]
    [Tooltip("Nombre exacto de la escena a la que se vuelve - debe coincidir con la escena donde vive StartupFlowUI.")]
    [SerializeField] private string nombreEscenaMenu = "MainMenu";

    /// <summary>
    /// Conectar al boton "Volver a jugar" de los 3 paneles de resultado.
    /// Puramente local - cada cliente lo ejecuta sobre si mismo, sin pedirle
    /// permiso a nadie ni afectar a los demas jugadores.
    /// </summary>
    public void SolicitarVolverAJugar()
    {
        if (gameManager != null)
        {
            gameManager.OcultarPanelesLocalmente();
        }

        if (turnManager != null)
        {
            turnManager.MostrarMensajeEsperando();
        }

        // Si YO soy el host, se me reactiva mi boton de "Iniciar partida" -
        // el resto de los jugadores no tiene ese boton (nunca lo tuvieron),
        // asi que para ellos esto no hace nada mas alla de lo de arriba.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && deckManager != null)
        {
            deckManager.MostrarBotonIniciarPartida();
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
        // Marcamos esto ANTES de Shutdown() - sin esto, NetworkBootstrap
        // interpreta cualquier desconexion propia como si el host se
        // hubiera caido, y mostraria ese mensaje aunque nos fuimos nosotros
        // por decision propia.
        if (NetworkBootstrap.Instance != null)
        {
            NetworkBootstrap.Instance.SalidaVoluntaria = true;
        }

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