using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Los 5 desenlaces posibles:
/// 1) RondaGanadaJugador - un jugador cumplió la regla, pero NO es la ultima ronda.
/// 2) RondaGanadaBoss - el boss cumplió la regla, pero NO es la ultima ronda.
/// 3) VictoriaJugadores - se termino la ultima ronda y los jugadores ganaron 2+ rondas.
/// 4) DerrotaPorBoss - se termino la ultima ronda y el boss gano 2+ rondas.
/// 5) DerrotaPorSospecha - la barra de sospecha (SuspicionManager) llegó al máximo -
///    esto termina la partida ENTERA de una, sin importar en que ronda vamos.
/// </summary>
public enum ResultadoPartida
{
    EnCurso,
    RondaGanadaJugador,
    RondaGanadaBoss,
    VictoriaJugadores,
    DerrotaPorBoss,
    DerrotaPorSospecha
}

/// <summary>
/// Centraliza cuándo se gana o se pierde CADA RONDA, y el resultado final
/// de la partida (mejor de 3 rondas). DeckManager avisa cuando alguien
/// cumple la regla de victoria (DeclararVictoriaJugador/Boss) - este script
/// decide si eso significa "gano la ronda" (todavia quedan rondas) o
/// "se termino la partida" (era la ultima ronda), segun el marcador.
/// SuspicionManager avisa aparte (DeclararDerrotaPorSospecha) - eso SIEMPRE
/// termina la partida entera, sin importar en que ronda vamos.
/// Autoridad de servidor: solo el servidor decide, sincronizado a todos.
/// </summary>
public class GameManager : NetworkBehaviour
{
    [Header("Configuración de rondas")]
    [SerializeField] private int totalRondas = 3;
    [Tooltip("Cuantas rondas hace falta ganar para llevarse la partida entera (mejor de 3 = 2).")]
    [SerializeField] private int rondasParaGanarLaPartida = 2;

    [Header("Paneles finales (uno por desenlace final - deben empezar todos desactivados en la escena)")]
    [SerializeField] private GameObject panelVictoria;
    [SerializeField] private GameObject panelDerrotaPorBoss;
    [SerializeField] private GameObject panelDerrotaPorSospecha;

    [Header("Panel de RONDA ganada (compartido - jugador o boss, con el nombre correspondiente)")]
    [SerializeField] private GameObject panelRondaGanada;
    [SerializeField] private TMP_Text textoRondaGanada;
    [Tooltip("Solo se muestra interactuable para el host - los demas jugadores ven el panel, pero sin este boton.")]
    [SerializeField] private GameObject botonSiguienteRonda;

    [Header("Contador de rondas (solo informativo, por ahora)")]
    [SerializeField] private TMP_Text textoContadorRondas;

    [Header("Texto del panel de victoria final (opcional)")]
    [SerializeField] private TMP_Text textoNombreGanador;

    public AudioClip musicPruba;

    private readonly NetworkVariable<ResultadoPartida> resultado =
        new NetworkVariable<ResultadoPartida>(ResultadoPartida.EnCurso);

    // Solo tiene sentido cuando resultado == VictoriaJugadores/RondaGanadaJugador -
    // que slot ganó, para poder mostrar su nombre en el panel.
    private readonly NetworkVariable<int> slotGanador = new NetworkVariable<int>(-1);

    // Arranca en 1 (no en 0) - "ronda 1 de 3" desde el principio.
    private readonly NetworkVariable<int> rondaActual = new NetworkVariable<int>(1);
    private readonly NetworkVariable<int> rondasGanadasJugadores = new NetworkVariable<int>(0);
    private readonly NetworkVariable<int> rondasGanadasBoss = new NetworkVariable<int>(0);

    // Historial de CADA ronda individual, sincronizado a todos - 0=pendiente,
    // 1=la gano un jugador, 2=la gano el boss. A diferencia de los
    // contadores de arriba (que solo dicen "cuantas"), esto dice
    // "cual ronda en particular gano quien", para poder pintar la bolita
    // correcta en el slot correcto (incluso si alguien se conecta a mitad
    // de partida y necesita reconstruir el marcador visual de una).
    private readonly NetworkList<int> resultadosPorRonda = new NetworkList<int>();

    public ResultadoPartida Resultado => resultado.Value;

    public bool PartidaTerminada => resultado.Value != ResultadoPartida.EnCurso;

    public int RondaActual => rondaActual.Value;
    public int TotalRondas => totalRondas;
    public int RondasGanadasJugadores => rondasGanadasJugadores.Value;
    public int RondasGanadasBoss => rondasGanadasBoss.Value;

    /// <summary>Se dispara en TODOS los clientes cada vez que cambia el resultado.</summary>
    public event Action<ResultadoPartida> OnResultadoCambio;

    /// <summary>
    /// Se dispara en TODOS los clientes cada vez que se registra (o se
    /// resetea) el resultado de una ronda especifica - (indiceRonda,
    /// resultado: 0=pendiente, 1=jugador, 2=boss). Lo usa RoundIndicatorUI
    /// para saber que bolita poner en que slot, sin tener que consultar
    /// nada mas del juego en si.
    /// </summary>
    public event Action<int, int> OnResultadoRondaRegistrado;


    public override void OnNetworkSpawn()
    {
        resultado.OnValueChanged += (anterior, nuevo) =>
        {
            ActualizarPaneles(nuevo);
            OnResultadoCambio?.Invoke(nuevo);
            ReiniciarMarcadorSiEsResultadoFinal(nuevo);
        };

        rondaActual.OnValueChanged += (anterior, nuevo) => ActualizarContadorRondas();

        // Falta esta: sin ella, el nombre del ganador en el panel de ronda
        // solo se recalculaba "de casualidad" cuando el callback de
        // resultado se disparaba - pero Netcode no garantiza que
        // slotGanador ya haya llegado/aplicado en ESE momento para los
        // clientes invitados (a diferencia del host, que no tiene ese
        // desfase de red). Sin esto, a veces se leia el valor viejo de una
        // ronda anterior, mostrando el nombre equivocado.
        slotGanador.OnValueChanged += (anterior, nuevo) => ActualizarPaneles(resultado.Value);

        resultadosPorRonda.OnListChanged += (cambio) =>
        {
            OnResultadoRondaRegistrado?.Invoke(cambio.Index, cambio.Value);
        };

        if (IsServer && resultadosPorRonda.Count == 0)
        {
            for (int i = 0; i < totalRondas; i++)
            {
                resultadosPorRonda.Add(0);
            }
        }

        ActualizarPaneles(resultado.Value);
        ActualizarContadorRondas();
    }

    /// <summary>0=pendiente, 1=la gano un jugador, 2=la gano el boss. indice 0-based.</summary>
    public int ObtenerResultadoDeRonda(int indiceRonda)
    {
        return indiceRonda >= 0 && indiceRonda < resultadosPorRonda.Count
            ? resultadosPorRonda[indiceRonda]
            : 0;
    }

    /// <summary>
    /// SOLO servidor. Apenas se ve un resultado FINAL (Victoria, Derrota
    /// por boss, o Derrota por sospecha) - NO una ronda ganada intermedia -
    /// el contador de rondas y las bolitas ya deben mostrar "partida nueva
    /// lista" (ronda 1/3, todo vacio), sin esperar a que el host apriete
    /// "Iniciar partida". Mismo criterio que el resto de la limpieza
    /// (mano, mazo, trampas, sospecha, boss): todo eso pasa apenas termina
    /// la partida, no recien al reiniciar.
    /// </summary>
    private void ReiniciarMarcadorSiEsResultadoFinal(ResultadoPartida nuevoResultado)
    {
        if (!IsServer)
        {
            return;
        }

        bool esResultadoFinal = nuevoResultado == ResultadoPartida.VictoriaJugadores
            || nuevoResultado == ResultadoPartida.DerrotaPorBoss
            || nuevoResultado == ResultadoPartida.DerrotaPorSospecha;

        if (!esResultadoFinal)
        {
            return;
        }

        rondaActual.Value = 1;
        rondasGanadasJugadores.Value = 0;
        rondasGanadasBoss.Value = 0;

        for (int i = 0; i < resultadosPorRonda.Count; i++)
        {
            resultadosPorRonda[i] = 0;
        }
    }

    /// <summary>
    /// SOLO desde el servidor. Vuelve TODO a cero: ronda 1, marcador 0-0,
    /// resultado En Curso - se usa para arrancar una partida COMPLETAMENTE
    /// nueva (boton "Iniciar partida" despues de un resultado final).
    /// </summary>
    public void ReiniciarPartidaCompleta()
    {
        if (!IsServer)
        {
            return;
        }

        rondaActual.Value = 1;
        rondasGanadasJugadores.Value = 0;
        rondasGanadasBoss.Value = 0;

        for (int i = 0; i < resultadosPorRonda.Count; i++)
        {
            resultadosPorRonda[i] = 0;
        }

        ReiniciarResultado();
    }

    /// <summary>
    /// SOLO desde el servidor (boton "Siguiente ronda", solo lo ve el
    /// host). Avanza a la ronda siguiente CONSERVANDO el marcador.
    /// </summary>
    public void AvanzarRonda()
    {
        if (!IsServer)
        {
            return;
        }

        rondaActual.Value++;

        ReiniciarResultado();
    }

    /// <summary>
    /// SOLO desde el servidor. Vuelve el resultado a En Curso, sin tocar
    /// ronda ni marcador - lo usan tanto ReiniciarPartidaCompleta() como
    /// AvanzarRonda(), y tambien TurnManager.IniciarPrimerTurno() (por las
    /// dudas, es un no-op si ya estaba en En Curso).
    /// </summary>
    public void ReiniciarResultado()
    {
        if (!IsServer)
        {
            return;
        }

        slotGanador.Value = -1;
        resultado.Value = ResultadoPartida.EnCurso;
    }

    /// <summary>SOLO desde el servidor (DeckManager), cuando un jugador cumple la regla activa.</summary>
    public void DeclararVictoriaJugador(int slot)
    {
        if (!IsServer || PartidaTerminada)
        {
            return;
        }

        slotGanador.Value = slot;
        rondasGanadasJugadores.Value++;
        RegistrarResultadoDeRonda(1);

        Debug.Log($"[Servidor] El slot {slot} cumplió la regla de victoria (ronda {rondaActual.Value}/{totalRondas}). Marcador: jugadores {rondasGanadasJugadores.Value} - boss {rondasGanadasBoss.Value}.");

        ResolverFinDeRonda(ganoJugador: true);
    }

    /// <summary>SOLO desde el servidor (DeckManager), cuando el boss cumple la regla activa.</summary>
    public void DeclararVictoriaBoss()
    {
        if (!IsServer || PartidaTerminada)
        {
            return;
        }

        rondasGanadasBoss.Value++;
        RegistrarResultadoDeRonda(2);

        Debug.Log($"[Servidor] El boss cumplió la regla de victoria (ronda {rondaActual.Value}/{totalRondas}). Marcador: jugadores {rondasGanadasJugadores.Value} - boss {rondasGanadasBoss.Value}.");

        ResolverFinDeRonda(ganoJugador: false);
    }

    /// <summary>SOLO servidor. Guarda en resultadosPorRonda[rondaActual - 1] quien gano ESTA ronda (1=jugador, 2=boss).</summary>
    private void RegistrarResultadoDeRonda(int resultadoRonda)
    {
        int indice = rondaActual.Value - 1;

        if (indice >= 0 && indice < resultadosPorRonda.Count)
        {
            resultadosPorRonda[indice] = resultadoRonda;
        }
    }

    /// <summary>
    /// Decide, despues de sumar el punto de la ronda, si la partida sigue
    /// (todavia quedan rondas -> muestra el panel de "ronda ganada" con
    /// boton "Siguiente ronda" solo para el host) o si esta era la ULTIMA
    /// ronda -> evalua el marcador total y dispara el panel final que
    /// corresponda (Victoria o Derrota por boss).
    /// </summary>
    private void ResolverFinDeRonda(bool ganoJugador)
    {
        bool esUltimaRonda = rondaActual.Value >= totalRondas;

        if (!esUltimaRonda)
        {
            resultado.Value = ganoJugador
                ? ResultadoPartida.RondaGanadaJugador
                : ResultadoPartida.RondaGanadaBoss;

            return;
        }

        if (rondasGanadasJugadores.Value >= rondasParaGanarLaPartida)
        {
            resultado.Value = ResultadoPartida.VictoriaJugadores;
        }
        else
        {
            resultado.Value = ResultadoPartida.DerrotaPorBoss;
        }

        Debug.Log($"[Servidor] Partida terminada tras {totalRondas} ronda(s). Resultado final: {resultado.Value}.");
    }

    /// <summary>SOLO desde el servidor (SuspicionManager), cuando la barra llega al máximo.</summary>
    public void DeclararDerrotaPorSospecha()
    {
        if (!IsServer || PartidaTerminada)
        {
            return;
        }

        resultado.Value = ResultadoPartida.DerrotaPorSospecha;

        Debug.Log("[Servidor] Derrota: la barra de sospecha llegó al máximo.");
    }

    /// <summary>
    /// Puramente local (no toca la NetworkVariable de resultado) - lo llama
    /// GameRestartManager cuando ESTE cliente aprieta "Volver a jugar"
    /// individualmente. Los demas clientes (que todavia no volvieron) siguen
    /// viendo su propio panel de resultado sin verse afectados. Se vuelve a
    /// mostrar solo si el resultado sincronizado cambia de verdad (por
    /// ejemplo, si alguien gana otra vez en la ronda nueva).
    /// </summary>
    public void OcultarPanelesLocalmente()
    {
        if (panelVictoria != null) panelVictoria.SetActive(false);
        if (panelDerrotaPorBoss != null) panelDerrotaPorBoss.SetActive(false);
        if (panelDerrotaPorSospecha != null) panelDerrotaPorSospecha.SetActive(false);
        if (panelRondaGanada != null) panelRondaGanada.SetActive(false);
    }

    private void ActualizarPaneles(ResultadoPartida nuevoResultado)
    {
        AvisarSiFalta(panelVictoria, nameof(panelVictoria));
        AvisarSiFalta(panelDerrotaPorBoss, nameof(panelDerrotaPorBoss));
        AvisarSiFalta(panelDerrotaPorSospecha, nameof(panelDerrotaPorSospecha));
        AvisarSiFalta(panelRondaGanada, nameof(panelRondaGanada));

        if (panelVictoria != null) panelVictoria.SetActive(nuevoResultado == ResultadoPartida.VictoriaJugadores);
        if (panelDerrotaPorBoss != null) panelDerrotaPorBoss.SetActive(nuevoResultado == ResultadoPartida.DerrotaPorBoss);
        if (panelDerrotaPorSospecha != null) panelDerrotaPorSospecha.SetActive(nuevoResultado == ResultadoPartida.DerrotaPorSospecha);

        bool esRondaGanada = nuevoResultado == ResultadoPartida.RondaGanadaJugador
            || nuevoResultado == ResultadoPartida.RondaGanadaBoss;

        if (panelRondaGanada != null) panelRondaGanada.SetActive(esRondaGanada);

        // El boton "Siguiente ronda" SOLO lo ve el host - a los demas
        // jugadores se les quita el panel entero cuando el host lo aprieta
        // (resultado vuelve a EnCurso), pero mientras tanto ellos ven el
        // panel SIN este boton.
        if (botonSiguienteRonda != null) botonSiguienteRonda.SetActive(esRondaGanada && IsServer);

        Debug.Log($"[GameManager] Resultado actualizado a: {nuevoResultado} (ronda {rondaActual.Value}/{totalRondas}, marcador jugadores {rondasGanadasJugadores.Value} - boss {rondasGanadasBoss.Value})");

        if (esRondaGanada && textoRondaGanada != null)
        {
            string nombre = nuevoResultado == ResultadoPartida.RondaGanadaBoss
                ? ObtenerNombreDelBoss()
                : ObtenerNombrePorSlot(slotGanador.Value);

            textoRondaGanada.text = $"¡{nombre} ganó la ronda {rondaActual.Value}!";
        }

        if (nuevoResultado == ResultadoPartida.VictoriaJugadores && textoNombreGanador != null)
        {
            string nombre = ObtenerNombrePorSlot(slotGanador.Value);
            textoNombreGanador.text = $"¡{nombre} ganó!";
        }
    }

    private void ActualizarContadorRondas()
    {
        if (textoContadorRondas != null)
        {
            textoContadorRondas.text = $"Ronda {rondaActual.Value}/{totalRondas}";
        }
    }

    private string ObtenerNombrePorSlot(int slot)
    {
        return PlayerNamePanelsUI.Instance != null
            ? PlayerNamePanelsUI.Instance.ObtenerNombrePorSlot(slot)
            : $"Jugador {slot}";
    }

    private string ObtenerNombreDelBoss()
    {
        return PlayerNamePanelsUI.Instance != null
            ? PlayerNamePanelsUI.Instance.ObtenerNombreDelBoss()
            : "El Boss";
    }

    private void AvisarSiFalta(GameObject panel, string nombreCampo)
    {
        if (panel == null)
        {
            Debug.LogWarning($"[GameManager] El campo '{nombreCampo}' no está asignado en el Inspector - ese panel nunca se va a poder mostrar.");
        }
    }
}