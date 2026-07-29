using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Los 3 desenlaces posibles de la partida:
/// 1) VictoriaJugadores - algún jugador cumplió la regla de victoria.
/// 2) DerrotaPorBoss - el boss cumplió la regla de victoria primero.
/// 3) DerrotaPorSospecha - la barra de sospecha (SuspicionManager) llegó al máximo.
/// </summary>
public enum ResultadoPartida
{
    EnCurso,
    VictoriaJugadores,
    DerrotaPorBoss,
    DerrotaPorSospecha
}

/// <summary>
/// Centraliza cuándo se gana o se pierde la partida. Antes esto vivía
/// repartido (TurnManager.DeclararGanador, y nada para la sospecha) - ahora
/// DeckManager y SuspicionManager avisan aquí, y este decide qué panel
/// mostrar. Autoridad de servidor: solo el servidor decide el resultado,
/// sincronizado a todos para que el panel correcto aparezca en cada cliente.
/// </summary>
public class GameManager : NetworkBehaviour
{
    [Header("Paneles (uno por desenlace - deben empezar todos desactivados en la escena)")]
    [SerializeField] private GameObject panelVictoria;
    [SerializeField] private GameObject panelDerrotaPorBoss;
    [SerializeField] private GameObject panelDerrotaPorSospecha;

    [Header("Texto del panel de victoria (opcional)")]
    [SerializeField] private TMP_Text textoNombreGanador;

    public AudioClip musicPruba;

    private readonly NetworkVariable<ResultadoPartida> resultado =
        new NetworkVariable<ResultadoPartida>(ResultadoPartida.EnCurso);

    // Solo tiene sentido cuando resultado == VictoriaJugadores - que slot
    // ganó, para poder mostrar su nombre en el panel.
    private readonly NetworkVariable<int> slotGanador = new NetworkVariable<int>(-1);

    public ResultadoPartida Resultado => resultado.Value;

    public bool PartidaTerminada => resultado.Value != ResultadoPartida.EnCurso;

    /// <summary>Se dispara en TODOS los clientes cada vez que cambia el resultado.</summary>
    public event Action<ResultadoPartida> OnResultadoCambio;


    public override void OnNetworkSpawn()
    {
        resultado.OnValueChanged += (anterior, nuevo) =>
        {
            ActualizarPaneles(nuevo);
            OnResultadoCambio?.Invoke(nuevo);
        };

        ActualizarPaneles(resultado.Value);
    }

    /// <summary>
    /// SOLO desde el servidor (TurnManager.IniciarPrimerTurno) - vuelve todo
    /// a EnCurso para que una ronda nueva pueda arrancar limpia.
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
        resultado.Value = ResultadoPartida.VictoriaJugadores;

        Debug.Log($"[Servidor] ¡Victoria de los jugadores! El slot {slot} cumplió la regla de victoria.");
    }

    /// <summary>SOLO desde el servidor (DeckManager), cuando el boss cumple la regla activa.</summary>
    public void DeclararVictoriaBoss()
    {
        if (!IsServer || PartidaTerminada)
        {
            return;
        }

        resultado.Value = ResultadoPartida.DerrotaPorBoss;

        Debug.Log("[Servidor] Derrota: el boss cumplió la regla de victoria primero.");
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

    private void ActualizarPaneles(ResultadoPartida nuevoResultado)
    {
        AvisarSiFalta(panelVictoria, nameof(panelVictoria));
        AvisarSiFalta(panelDerrotaPorBoss, nameof(panelDerrotaPorBoss));
        AvisarSiFalta(panelDerrotaPorSospecha, nameof(panelDerrotaPorSospecha));

        if (panelVictoria != null) panelVictoria.SetActive(nuevoResultado == ResultadoPartida.VictoriaJugadores);
        if (panelDerrotaPorBoss != null) panelDerrotaPorBoss.SetActive(nuevoResultado == ResultadoPartida.DerrotaPorBoss);
        if (panelDerrotaPorSospecha != null) panelDerrotaPorSospecha.SetActive(nuevoResultado == ResultadoPartida.DerrotaPorSospecha);

        Debug.Log($"[GameManager] Resultado actualizado a: {nuevoResultado}");

        if (nuevoResultado == ResultadoPartida.VictoriaJugadores && textoNombreGanador != null)
        {
            string nombre = PlayerNamePanelsUI.Instance != null
                ? PlayerNamePanelsUI.Instance.ObtenerNombrePorSlot(slotGanador.Value)
                : $"Jugador {slotGanador.Value}";

            textoNombreGanador.text = $"¡{nombre} ganó!";
        }
    }

    private void AvisarSiFalta(GameObject panel, string nombreCampo)
    {
        if (panel == null)
        {
            Debug.LogWarning($"[GameManager] El campo '{nombreCampo}' no está asignado en el Inspector - ese panel nunca se va a poder mostrar.");
        }
    }
}