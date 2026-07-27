using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// El boss como "un jugador más" dentro de la arquitectura de red real:
/// mismo mazo compartido, misma mano por cliente (con una identidad
/// reservada, BOSS_ID, dentro de DeckManager), mismo turno (un slot más en
/// la rotación de TurnManager). La lógica de decisión corre ÚNICAMENTE en
/// el servidor - nunca existe una versión "de cliente" jugando por él.
///
/// Sin representación de su mano real por ahora (ningún jugador ve la mano
/// de otro en esta arquitectura) - pero SÍ hay un indicador visual simple
/// (que cartas tenga 4 o 5), sincronizado a todos, para que se note cuándo
/// robó y cuándo descartó sin exponer identidad de ninguna carta.
/// </summary>
public class BossManager : NetworkBehaviour
{
    [Header("Referencias")]
    [SerializeField] private DeckManager deckManager;
    [SerializeField] private TurnManager turnManager;

    [Header("Ritmo del boss")]
    [Tooltip("Espera artificial antes de robar/descartar en su turno. También sirve como perilla de dificultad.")]
    [SerializeField] private float delayAntesDeActuar = 1.5f;
    [Tooltip("Espera entre cada carta durante el reparto inicial (mismo propósito que delayBetweenCards en DeckManager).")]
    [SerializeField] private float delayEntreCartasReparto = 0.25f;

    [Header("Visual (sincronizado a todos los clientes)")]
    [SerializeField] private Image bossImage;
    [SerializeField] private Sprite spriteConCuatroCartas;
    [SerializeField] private Sprite spriteConCincoCartas;

    [Header("Identidad")]
    [Tooltip("Fijo por ahora ('Boss'). Más adelante será la leyenda sorteada de la ronda, mismo patrón que la regla de victoria.")]
    [SerializeField] private string nombreInicial = "Boss";

    // Sincronizado a todos - así el panel del boss muestra el mismo nombre
    // en cualquier cliente, sin importar cuándo se conecte.
    private readonly NetworkVariable<FixedString64Bytes> nombreBoss = new NetworkVariable<FixedString64Bytes>();

    // Puramente cosmético - NO revela identidad de ninguna carta, solo si
    // el boss "tiene una de más" (recién robó, todavía no descartó). Se
    // sincroniza a todos porque, a diferencia de la mano real, esto no es
    // secreto - es como el contador del mazo (cartasEnMazo en DeckManager).
    private readonly NetworkVariable<bool> tieneCincoCartas = new NetworkVariable<bool>(false);

    public override void OnNetworkSpawn()
    {
        // El sprite se actualiza en TODOS los clientes, no solo el servidor -
        // por eso esto va antes del "if (!IsServer) return;".
        tieneCincoCartas.OnValueChanged += (anterior, nuevo) => ActualizarSprite(nuevo);
        ActualizarSprite(tieneCincoCartas.Value);

        // Mismo motivo: el nombre debe verse igual en todos los clientes.
        nombreBoss.OnValueChanged += (anterior, nuevo) => AvisarNombreAlPanel(nuevo.ToString());

        if (IsServer)
        {
            nombreBoss.Value = nombreInicial;
        }

        // Por si el valor ya estaba sincronizado antes de suscribirnos
        // (por ejemplo, un cliente que se conecta a mitad de partida).
        AvisarNombreAlPanel(nombreBoss.Value.ToString());

        if (!IsServer)
        {
            return; // la LÓGICA del boss no existe del lado del cliente
        }

        if (turnManager != null)
        {
            turnManager.OnBossTurnStarted += JugarTurno;
        }
    }

    /// <summary>
    /// Llamado desde PlayerNamePanelsUI.Awake() por si ese panel todavía no
    /// existía cuando este objeto de red terminó de spawnear - mismo patrón
    /// que PlayerCube.ReintentarAvisoDePanel().
    /// </summary>
    public void ReintentarAvisoDePanel()
    {
        AvisarNombreAlPanel(nombreBoss.Value.ToString());
    }

    private void AvisarNombreAlPanel(string nombre)
    {
        if (PlayerNamePanelsUI.Instance != null && !string.IsNullOrEmpty(nombre))
        {
            PlayerNamePanelsUI.Instance.ActualizarNombreBoss(nombre);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (turnManager != null)
        {
            turnManager.OnBossTurnStarted -= JugarTurno;
        }
    }

    private void ActualizarSprite(bool cincoCartas)
    {
        if (bossImage == null)
        {
            return;
        }

        bossImage.sprite = cincoCartas ? spriteConCincoCartas : spriteConCuatroCartas;
    }

    /// <summary>
    /// Llamado por DeckManager.OnIniciarPartidaPressed() al arrancar la
    /// partida - reparte la mano inicial al boss, igual que
    /// RepartirManoAJugador() hace con cada cliente real.
    /// </summary>
    public void IniciarManoInicial()
    {
        if (!IsServer)
        {
            return;
        }

        StartCoroutine(RepartirManoInicial());
    }

    private IEnumerator RepartirManoInicial()
    {
        int cantidad = deckManager != null ? deckManager.InitialHandSize : 0;

        for (int i = 0; i < cantidad; i++)
        {
            yield return new WaitForSeconds(delayEntreCartasReparto);

            CardData cartaRepartida = deckManager.RobarCartaInicialBoss();

            if (cartaRepartida == null)
            {
                Debug.LogWarning("[Boss] El mazo se quedó sin cartas durante el reparto inicial.");
                yield break;
            }
        }

        Debug.Log("[Boss] Reparto inicial terminado. Tiene " + deckManager.ObtenerManoDelBoss().Count + " carta(s).");
    }

    /// <summary>
    /// Punto de entrada del turno del boss - se dispara solo, por el evento
    /// TurnManager.OnBossTurnStarted, cuando la rotación de turnos le llega.
    /// </summary>
    public void JugarTurno()
    {
        StartCoroutine(EjecutarTurno());
    }

    private IEnumerator EjecutarTurno()
    {
        if (deckManager == null || turnManager == null)
        {
            Debug.LogError("[BossManager] Faltan referencias de DeckManager o TurnManager.");
            yield break;
        }

        Debug.Log("[Boss] Empieza su turno.");

        yield return new WaitForSeconds(delayAntesDeActuar);

        // --- Robar --- (RobarCartaParaBoss ya avisa a TurnManager.NotificarRoboRealizado())
        CardData cartaRobada = deckManager.RobarCartaParaBoss();

        if (cartaRobada == null)
        {
            Debug.LogWarning("[Boss] No pudo robar (mazo vacío). El turno queda trabado - revisar reciclado de descarte.");
            yield break;
        }

        // Recién robó - visualmente pasa a tener "una de más".
        tieneCincoCartas.Value = true;

        yield return new WaitForSeconds(delayAntesDeActuar);

        // --- Evaluar y descartar ---
        List<int> mano = deckManager.ObtenerManoDelBoss();
        int cardIdADescartar = BossStrategy.ElegirCartaADescartar(mano, turnManager.ReglaActiva);

        if (cardIdADescartar < 0)
        {
            Debug.LogError("[Boss] BossStrategy no devolvió un cardId válido.");
            yield break;
        }

        // DescartarCartaDelBoss ya se encarga de: sumar a discardPile,
        // avisar a todos vía MostrarCartaDescartadaClientRpc (aparece en la
        // mesa de todos), revisar VictoryRules, y avanzar el turno.
        deckManager.DescartarCartaDelBoss(cardIdADescartar);

        // Recién descartó - vuelve a su cantidad "normal" de cartas.
        tieneCincoCartas.Value = false;

        Debug.Log("[Boss] Termina su turno.");
    }
}