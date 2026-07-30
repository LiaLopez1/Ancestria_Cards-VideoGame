using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
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
/// de otro en esta arquitectura) - pero SÍ hay dos pares de sprites
/// sincronizados a todos, sin exponer identidad de ninguna carta:
/// - En SU TURNO: 4/5 cartas (roba/descarta).
/// - FUERA de su turno: revisando cartas / mirando oponentes - alterna solo,
///   y de esto depende cuánta sospecha suma un jugador que hace trampa en
///   ese momento (ver SuspicionManager).
/// </summary>
public class BossManager : NetworkBehaviour
{
    /// <summary>En qué está "ocupado" el boss mientras NO es su turno.</summary>
    public enum EstadoAtencionBoss
    {
        RevisandoCartas,
        MirandoOponentes
    }

    [Header("Referencias")]
    [SerializeField] private DeckManager deckManager;
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private SuspicionManager suspicionManager;
    [Tooltip("Necesario para que el boss sepa cuál es la categoría infiltrada cuando esa regla está activa.")]
    [SerializeField] private InfiltratedCardManager infiltratedCardManager;

    [Header("Ritmo del boss")]
    [Tooltip("Espera artificial antes de robar/descartar en su turno. También sirve como perilla de dificultad.")]
    [SerializeField] private float delayAntesDeActuar = 1.5f;
    [Tooltip("Espera entre cada carta durante el reparto inicial (mismo propósito que delayBetweenCards en DeckManager).")]
    [SerializeField] private float delayEntreCartasReparto = 0.25f;

    [Header("Visual: en su turno (sincronizado a todos)")]
    [SerializeField] private Image bossImage;
    [SerializeField] private Sprite spriteConCuatroCartas;
    [SerializeField] private Sprite spriteConCincoCartas;

    [Header("Visual: fuera de su turno - estado de atención")]
    [SerializeField] private Sprite spriteRevisandoCartas;
    [SerializeField] private Sprite spriteMirandoOponentes;
    [Tooltip("Rango de segundos entre cada cambio de atención (aleatorio dentro de este rango).")]
    [SerializeField] private float intervaloMinimoAtencion = 2f;
    [SerializeField] private float intervaloMaximoAtencion = 5f;

    [Header("Visual: forma monstruo")]
    [SerializeField] private Sprite spriteMonstruoConCuatroCartas;
    [SerializeField] private Sprite spriteMonstruoConCincoCartas;
    [SerializeField] private Sprite spriteMonstruoRevisandoCartas;
    [SerializeField] private Sprite spriteMonstruoMirandoOponentes;

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

    // Igual de cosmético, pero relevante para la mecánica de sospecha:
    // SuspicionManager consulta AtencionActual para saber cuánto sumar
    // cuando alguien hace trampa en ese instante.
    private readonly NetworkVariable<EstadoAtencionBoss> atencionActual =
        new NetworkVariable<EstadoAtencionBoss>(EstadoAtencionBoss.MirandoOponentes);

    public EstadoAtencionBoss AtencionActual => atencionActual.Value;

    // Referencia a la corrutina de alternancia de atencion, para poder
    // reiniciarla limpio si IniciarManoInicial() se llama de nuevo (una
    // ronda nueva) sin dejar una copia vieja corriendo en paralelo.
    private Coroutine corrutinaAtencion;
    private Sprite spriteInicialBoss;

    private readonly NetworkVariable<bool> partidaIniciada = new NetworkVariable<bool>(false);

    private void Awake()
    {
        if (bossImage != null)
        {
            spriteInicialBoss = bossImage.sprite;
        }
    }


    public override void OnNetworkSpawn()
    {
        // El sprite se actualiza en TODOS los clientes, no solo el servidor -
        // por eso esto va antes del "if (!IsServer) return;". Se recalcula
        // cuando cambia CUALQUIERA de las tres cosas que lo afectan: si tiene
        // 5 cartas, su estado de atención, o de quién es el turno ahora.
        tieneCincoCartas.OnValueChanged += (anterior, nuevo) => ActualizarSpriteBoss();
        atencionActual.OnValueChanged += (anterior, nuevo) => ActualizarSpriteBoss();
        partidaIniciada.OnValueChanged += (anterior, nuevo) => ActualizarSpriteBoss();

        if (turnManager != null)
        {
            turnManager.OnEstadoTurnoCambio += ActualizarSpriteBoss;
        }

        if (suspicionManager != null)
        {
            suspicionManager.OnSospechaCambio += AlCambiarSospecha;
        }

        ActualizarSpriteBoss();

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

    public override void OnNetworkDespawn()
    {
        if (turnManager != null)
        {
            turnManager.OnBossTurnStarted -= JugarTurno;
            turnManager.OnEstadoTurnoCambio -= ActualizarSpriteBoss;
        }

        if (suspicionManager != null)
        {
            suspicionManager.OnSospechaCambio -= AlCambiarSospecha;
        }
    }

    /// <summary>
    /// SOLO el servidor la corre, y SOLO arranca desde que la partida
    /// realmente empieza (IniciarManoInicial) - antes de eso el boss se
    /// queda fijo en "Mirando oponentes". Cada tanto (intervalo aleatorio),
    /// si NO es el turno del boss, alterna su estado de atención - se pausa
    /// sola durante su propio turno (ahí ya está "ocupado" robando/
    /// descartando, mostrando el otro par de sprites).
    /// </summary>
    private IEnumerator AlternarAtencionMientrasNoEsSuTurno()
    {
        while (true)
        {
            float espera = UnityEngine.Random.Range(intervaloMinimoAtencion, intervaloMaximoAtencion);
            yield return new WaitForSeconds(espera);

            if (turnManager != null && turnManager.EsTurnoDelBoss())
            {
                continue; // ocupado en su propio turno - no alterna
            }

            atencionActual.Value = atencionActual.Value == EstadoAtencionBoss.RevisandoCartas
                ? EstadoAtencionBoss.MirandoOponentes
                : EstadoAtencionBoss.RevisandoCartas;
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

    /// <summary>
    /// Decide qué sprite mostrar: si es su turno, el par de robar/descartar;
    /// si no, el par de atención (revisando/mirando) - un solo Image, pero
    /// la fuente depende de en qué "fase" está el boss ahora mismo.
    /// </summary>
    private void ActualizarSpriteBoss()
    {
        if (bossImage == null)
        {
            return;
        }

        // Antes de comenzar la partida conserva el sprite original.
        if (!partidaIniciada.Value)
        {
            bossImage.sprite = spriteInicialBoss;
            return;
        }

        if (turnManager == null)
        {
            return;
        }

        bool usarFormaMonstruo =
            suspicionManager != null &&
            suspicionManager.AlcanzoMitadDeSospecha;

        if (turnManager.EsTurnoDelBoss())
        {
            if (tieneCincoCartas.Value)
            {
                bossImage.sprite = usarFormaMonstruo ? spriteMonstruoConCincoCartas:spriteConCincoCartas;
            }
            else
            {
                bossImage.sprite = usarFormaMonstruo? spriteMonstruoConCuatroCartas : spriteConCuatroCartas;
            }
        }
        else
        {
            if (atencionActual.Value == EstadoAtencionBoss.RevisandoCartas)
            {
                bossImage.sprite = usarFormaMonstruo ? spriteMonstruoRevisandoCartas : spriteRevisandoCartas;
            }
            else
            {
                bossImage.sprite = usarFormaMonstruo ? spriteMonstruoMirandoOponentes : spriteMirandoOponentes;
            }
        }
    }

//para  cambiar los sprites del boss
    private void AlCambiarSospecha(float nuevoNivel)
    {
        ActualizarSpriteBoss();
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
        // Recien ahora arranca de verdad la partida - nos aseguramos de
        // empezar (o volver a empezar, si es una ronda nueva) quieto en
        // "Mirando oponentes", y ahi si arrancamos el ciclo de alternancia.
        // Antes de este punto, el boss se queda fijo en ese estado inicial.
        atencionActual.Value = EstadoAtencionBoss.MirandoOponentes;

        partidaIniciada.Value = true;

        if (corrutinaAtencion != null)
        {
            StopCoroutine(corrutinaAtencion);
        }

        corrutinaAtencion = StartCoroutine(AlternarAtencionMientrasNoEsSuTurno());

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

    /// <summary>
    /// Solo para diagnóstico en consola - convierte una mano (cardIds) en
    /// nombres legibles, para poder verificar a ojo que BossStrategy elige
    /// bien. Fácil de sacar más adelante si ya no hace falta.
    /// </summary>
    private string NombresDeMano(List<int> mano)
    {
        List<string> nombres = new List<string>();

        foreach (int id in mano)
        {
            CardData carta = CardDatabase.Instance.ObtenerPorId(id);
            nombres.Add(carta != null ? $"{carta.cardName} ({carta.category}, id={id})" : $"id={id} (desconocida)");
        }

        return "[" + string.Join(" | ", nombres) + "]";
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

        Debug.Log("[Boss][Diagnóstico] Regla activa: " + VictoryRules.ObtenerNombre(turnManager.ReglaActiva)
            + " | Mano completa: " + NombresDeMano(mano));

        CardCategory? categoriaInfiltrada = infiltratedCardManager != null && infiltratedCardManager.HayCategoriaInfiltrada
            ? infiltratedCardManager.CategoriaInfiltrada
            : (CardCategory?)null;

        int cardIdADescartar = BossStrategy.ElegirCartaADescartar(mano, turnManager.ReglaActiva, categoriaInfiltrada);

        if (cardIdADescartar < 0)
        {
            Debug.LogError("[Boss] BossStrategy no devolvió un cardId válido.");
            yield break;
        }

        CardData cartaElegida = CardDatabase.Instance.ObtenerPorId(cardIdADescartar);
        Debug.Log("[Boss][Diagnóstico] Elige descartar: " + (cartaElegida != null ? cartaElegida.cardName : "?") + " (cardId=" + cardIdADescartar + ")");

        // DescartarCartaDelBoss ya se encarga de: sumar a discardPile,
        // avisar a todos vía MostrarCartaDescartadaClientRpc (aparece en la
        // mesa de todos), revisar VictoryRules, y avanzar el turno.
        deckManager.DescartarCartaDelBoss(cardIdADescartar);

        // Recién descartó - vuelve a su cantidad "normal" de cartas.
        tieneCincoCartas.Value = false;

        Debug.Log("[Boss] Termina su turno.");
    }
}