using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pone a jugar a los 2 bots. Va UNA vez en cada escena de jefe (como
/// prefab) y solo actúa si la partida es local: si NetworkBootstrap.
/// ModoLocalConBots es false (partida online), se apaga solo en Awake y no
/// hace absolutamente nada.
///
/// Corre únicamente en el servidor (que en modo local es la propia máquina
/// del jugador) y habla con los mismos managers que usa un humano:
///   - DeckManager (partial, ver DeckManager.Bots.cs): robar y descartar.
///   - TrapManager.DeclararComoBot: pedir / mostrar categoría.
///   - SuspicionManager: suma sospecha igual que si fuera un jugador.
///
/// Las referencias se buscan solas al iniciar (FindFirstObjectByType), así
/// no hay que cablearlas en cada una de las escenas de jefe.
///
/// Dos "relojes" independientes:
///   1) El turno del bot (corrutina): robar -> (intercambio) -> descartar.
///   2) El bucle de pensar (corrutina): cada 1.5-3 s cada bot decide si
///      pide o muestra una categoría. Así no reaccionan instantáneamente.
/// </summary>
public class BotController : MonoBehaviour
{
    [Header("Perfiles (bot 1 = slot 1, bot 2 = slot 2)")]
    [SerializeField] private BotPerfil perfilBot1 = new BotPerfil { nombre = "Bot Ana", audacia = 0.3f, sospechaTope = 0.6f };
    [SerializeField] private BotPerfil perfilBot2 = new BotPerfil { nombre = "Bot Luis", audacia = 0.7f, sospechaTope = 0.8f };

    [Header("Ritmo (segundos: mínimo, máximo)")]
    [SerializeField] private Vector2 esperaAntesDeRobar = new Vector2(1.2f, 2.5f);
    [SerializeField] private Vector2 esperaAntesDeDescartar = new Vector2(1.2f, 2.5f);
    [SerializeField] private Vector2 intervaloPensarTrampas = new Vector2(1.5f, 3f);
    [SerializeField] private Vector2 deliberacionIntercambio = new Vector2(1.5f, 3f);

    [Header("Intercambios")]
    [Tooltip("Reservado: déjalo apagado hasta que TradeManager soporte bots. Apagado, los bots solo intercambian entre ellos.")]
    [SerializeField] private bool intercambiosConHumano = false;

    private DeckManager deckManager;
    private TurnManager turnManager;
    private SuspicionManager suspicionManager;
    private TrapManager trapManager;
    private BossManager bossManager;
    private InfiltratedCardManager infiltratedCardManager;
    private GameManager gameManager;

    private BotBrain[] cerebros;
    private Coroutine corrutinaPensar;
    private Coroutine corrutinaTurno;

    // Sube cada vez que cambia el resultado de la partida. Las corrutinas
    // lo guardan al arrancar y se abortan si ya no coincide (ronda terminada
    // o reiniciada mientras el bot "pensaba").
    private int generacion;
    private bool listo;

    private void Awake()
    {
        if (!NetworkBootstrap.ModoLocalConBots)
        {
            gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        StartCoroutine(Inicializar());
    }

    private IEnumerator Inicializar()
    {
        deckManager = FindFirstObjectByType<DeckManager>();
        turnManager = FindFirstObjectByType<TurnManager>();
        suspicionManager = FindFirstObjectByType<SuspicionManager>();
        trapManager = FindFirstObjectByType<TrapManager>();
        bossManager = FindFirstObjectByType<BossManager>();
        infiltratedCardManager = FindFirstObjectByType<InfiltratedCardManager>();
        gameManager = FindFirstObjectByType<GameManager>();

        if (deckManager == null || turnManager == null || suspicionManager == null
            || trapManager == null || gameManager == null)
        {
            Debug.LogError("[Bots] Falta algún manager en la escena (DeckManager, TurnManager, SuspicionManager, TrapManager o GameManager). Los bots no van a jugar.");
            yield break;
        }

        // Esperamos a que Netcode haya hecho "spawn" de los objetos de la escena.
        yield return new WaitUntil(() =>
            NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsServer
            && deckManager.IsSpawned
            && turnManager.IsSpawned
            && trapManager.IsSpawned
            && gameManager.IsSpawned);

        BotPerfil[] perfiles = { perfilBot1, perfilBot2 };
        cerebros = new BotBrain[BotIds.Cantidad];

        for (int i = 0; i < BotIds.Cantidad; i++)
        {
            int slot = BotIds.PrimerSlot + i;
            cerebros[i] = new BotBrain(perfiles[i], slot);

            // Los paneles de nombre de los slots 1 y 2 quedan para los bots.
            PlayerNamePanelsUI.Instance?.ActualizarNombre(slot, perfiles[i].nombre);
        }

        turnManager.OnBotTurnStarted += AlTurnoDeBot;
        trapManager.OnTrampaDeclarada += AlDeclararTrampa;
        gameManager.OnResultadoCambio += AlCambiarResultado;

        listo = true;
        corrutinaPensar = StartCoroutine(BucleDePensar());

        Debug.Log("[Bots] Listos: " + perfilBot1.nombre + " (slot 1) y " + perfilBot2.nombre + " (slot 2).");
    }

    private void OnDestroy()
    {
        if (turnManager != null) turnManager.OnBotTurnStarted -= AlTurnoDeBot;
        if (trapManager != null) trapManager.OnTrampaDeclarada -= AlDeclararTrampa;
        if (gameManager != null) gameManager.OnResultadoCambio -= AlCambiarResultado;
    }

    // ------------------------------------------------------------------
    // Eventos
    // ------------------------------------------------------------------

    /// <summary>Información pública: todos los bots se enteran de lo que pide o muestra cualquiera.</summary>
    private void AlDeclararTrampa(int slot, CardCategory categoria, bool tieneCategoria)
    {
        float ahora = Time.time;

        foreach (BotBrain cerebro in cerebros)
        {
            if (tieneCategoria)
            {
                cerebro.RegistrarMuestra(slot, categoria, ahora);
            }
            else
            {
                cerebro.RegistrarPedido(slot, categoria, ahora);
            }
        }
    }

    private void AlCambiarResultado(ResultadoPartida resultado)
    {
        generacion++;

        if (corrutinaTurno != null)
        {
            StopCoroutine(corrutinaTurno);
            corrutinaTurno = null;
        }

        if (resultado == ResultadoPartida.EnCurso)
        {
            foreach (BotBrain cerebro in cerebros)
            {
                cerebro.Reiniciar();
            }
        }
    }

    private void AlTurnoDeBot(int slot)
    {
        if (!listo) return;

        if (corrutinaTurno != null)
        {
            StopCoroutine(corrutinaTurno);
        }

        corrutinaTurno = StartCoroutine(JugarTurnoDeBot(slot, generacion));
    }

    // ------------------------------------------------------------------
    // Turno del bot: robar -> (intercambio) -> descartar
    // ------------------------------------------------------------------

    private IEnumerator JugarTurnoDeBot(int slot, int gen)
    {
        BotBrain cerebro = cerebros[slot - BotIds.PrimerSlot];
        ulong botId = BotIds.IdDeSlot(slot);

        yield return Esperar(esperaAntesDeRobar);

        if (!Vigente(gen)) yield break;

        CardData robada = deckManager.RobarCartaParaBot(botId, slot);

        if (robada == null)
        {
            Debug.LogWarning($"[Bots] {cerebro.Nombre} no pudo robar.");
            yield break;
        }

        // Ventana entre robar y descartar: único momento donde puede intercambiar.
        yield return Esperar(esperaAntesDeDescartar);

        if (!Vigente(gen)) yield break;

        VictoryRuleType regla = turnManager.ReglaActiva;
        CardCategory? infiltrada = CategoriaInfiltrada();
        List<int> mano = deckManager.ObtenerManoDeBot(botId);

        if (!suspicionManager.IntercambioEnCurso
            && cerebro.DebeProponerIntercambio(mano, regla, infiltrada, SlotsObjetivoPara(slot),
                SospechaNormalizada(), BossMiraAlFrente(), Time.time,
                out int slotObjetivo, out int cardIdOfrecido))
        {
            if (BotIds.EsSlotDeBot(slotObjetivo))
            {
                yield return StartCoroutine(IntercambioEntreBots(cerebro, slotObjetivo, cardIdOfrecido, gen));

                if (!Vigente(gen)) yield break;
            }
            else
            {
                Debug.Log($"[Bots] {cerebro.Nombre} quería intercambiar con el slot {slotObjetivo}, pero los intercambios con humano aún no están habilitados.");
            }
        }

        // La mano pudo cambiar con el intercambio: se vuelve a leer antes de descartar.
        mano = deckManager.ObtenerManoDeBot(botId);

        int cardIdADescartar = cerebro.ElegirDescarte(mano, regla, infiltrada);

        if (cardIdADescartar < 0)
        {
            Debug.LogError($"[Bots] {cerebro.Nombre}: BotBrain no devolvió una carta válida para descartar.");
            yield break;
        }

        deckManager.DescartarCartaDeBot(botId, slot, cardIdADescartar);
    }

    /// <summary>
    /// Intercambio bot -> bot, resuelto directo en el servidor. La sospecha
    /// sube mientras "deliberan", igual que en un intercambio de humanos
    /// (IniciarIntercambioRpc empieza a sumar, Aceptar/Cancelar la frena).
    /// </summary>
    private IEnumerator IntercambioEntreBots(BotBrain iniciador, int slotObjetivo, int cardIdOfrecido, int gen)
    {
        BotBrain objetivo = cerebros[slotObjetivo - BotIds.PrimerSlot];
        ulong idIniciador = BotIds.IdDeSlot(iniciador.MiSlot);
        ulong idObjetivo = BotIds.IdDeSlot(slotObjetivo);

        Debug.Log($"[Bots] {iniciador.Nombre} propone un intercambio a {objetivo.Nombre}.");

        suspicionManager.IniciarIntercambioRpc();
        iniciador.RegistrarTrampaHecha(Time.time);

        yield return Esperar(deliberacionIntercambio);

        if (!Vigente(gen)) yield break;

        if (!objetivo.AceptaIntercambio(iniciador.MiSlot, SospechaNormalizada(), Time.time))
        {
            Debug.Log($"[Bots] {objetivo.Nombre} rechazó el intercambio.");
            suspicionManager.CancelarIntercambioRpc();
            yield break;
        }

        List<int> manoObjetivo = deckManager.ObtenerManoDeBot(idObjetivo);
        int cardIdObjetivo = objetivo.ElegirDescarte(manoObjetivo, turnManager.ReglaActiva, CategoriaInfiltrada());

        suspicionManager.AceptarIntercambioRpc();

        bool exito = deckManager.EjecutarIntercambio(idIniciador, cardIdOfrecido, idObjetivo, cardIdObjetivo);

        Debug.Log(exito
            ? $"[Bots] Intercambio hecho entre {iniciador.Nombre} y {objetivo.Nombre}."
            : "[Bots] El intercambio falló (alguna carta ya no estaba disponible).");
    }

    // ------------------------------------------------------------------
    // Pensar: pedir / mostrar categorías fuera de su turno
    // ------------------------------------------------------------------

    private IEnumerator BucleDePensar()
    {
        while (true)
        {
            yield return Esperar(intervaloPensarTrampas);

            if (!listo || gameManager.PartidaTerminada) continue;

            foreach (BotBrain cerebro in cerebros)
            {
                PensarTrampas(cerebro);
            }
        }
    }

    private void PensarTrampas(BotBrain cerebro)
    {
        int slot = cerebro.MiSlot;

        // Durante su propio turno no pide ni muestra: ahí decide el intercambio.
        if (turnManager.EsTurnoDelSlot(slot)) return;

        List<int> mano = deckManager.ObtenerManoDeBot(BotIds.IdDeSlot(slot));

        // Solo con la mano completa (después del reparto inicial).
        if (mano.Count != deckManager.InitialHandSize) return;

        VictoryRuleType regla = turnManager.ReglaActiva;
        CardCategory? infiltrada = CategoriaInfiltrada();
        float sospecha = SospechaNormalizada();
        bool bossMira = BossMiraAlFrente();
        float ahora = Time.time;

        if (cerebro.DebePedirCategoria(mano, regla, infiltrada, sospecha, bossMira, ahora, out CardCategory categoriaPedida))
        {
            // Primero la sospecha (depende de qué mira el boss ahora mismo), luego lo público.
            suspicionManager.SolicitarTrampaPedirRpc();
            cerebro.RegistrarTrampaHecha(ahora);
            trapManager.DeclararComoBot(slot, categoriaPedida, false);
            return; // una sola trampa por tanda de pensar
        }

        if (cerebro.DebeMostrarCategoria(mano, regla, infiltrada, sospecha, bossMira, ahora,
            out CardCategory categoriaMostrada, out int slotPedidor))
        {
            suspicionManager.SolicitarTrampaDecirRpc();
            cerebro.RegistrarTrampaHecha(ahora);
            cerebro.RegistrarMuestraPropia(slotPedidor, ahora);
            trapManager.DeclararComoBot(slot, categoriaMostrada, true);
        }
    }

    // ------------------------------------------------------------------
    // Utilidades
    // ------------------------------------------------------------------

    private bool Vigente(int gen)
    {
        return gen == generacion && !gameManager.PartidaTerminada;
    }

    private static WaitForSeconds Esperar(Vector2 rango)
    {
        return new WaitForSeconds(Random.Range(rango.x, rango.y));
    }

    private float SospechaNormalizada()
    {
        return suspicionManager.SospechaMaxima > 0f
            ? suspicionManager.NivelSospecha / suspicionManager.SospechaMaxima
            : 0f;
    }

    private bool BossMiraAlFrente()
    {
        return bossManager != null
            && bossManager.AtencionActual == BossManager.EstadoAtencionBoss.MirandoOponentes;
    }

    private CardCategory? CategoriaInfiltrada()
    {
        return infiltratedCardManager != null && infiltratedCardManager.HayCategoriaInfiltrada
            ? infiltratedCardManager.CategoriaInfiltrada
            : (CardCategory?)null;
    }

    /// <summary>Slots a los que este bot puede proponerle un intercambio (sin él mismo y sin el boss).</summary>
    private List<int> SlotsObjetivoPara(int slotBot)
    {
        List<int> slots = new List<int>();

        foreach (BotBrain cerebro in cerebros)
        {
            if (cerebro.MiSlot != slotBot)
            {
                slots.Add(cerebro.MiSlot);
            }
        }

        if (intercambiosConHumano)
        {
            slots.Add(0);
        }

        return slots;
    }
}