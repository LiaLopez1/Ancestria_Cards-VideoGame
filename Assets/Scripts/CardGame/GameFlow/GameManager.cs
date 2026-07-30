using System;
using System.Collections;
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
/// Los paneles hacen fade in/out (CanvasGroup) en vez de aparecer/desaparecer
/// de golpe.
/// </summary>
public class GameManager : NetworkBehaviour
{
    [Header("Paneles (uno por desenlace - deben empezar todos desactivados en la escena)")]
    [SerializeField] private CanvasGroup panelVictoria;
    [SerializeField] private CanvasGroup panelDerrotaPorBoss;
    [SerializeField] private CanvasGroup panelDerrotaPorSospecha;

    [Header("Transicion de paneles")]
    [SerializeField] private float fadeDuration = 0.4f;

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
        if (panelVictoria != null) StartCoroutine(FadeOutPanel(panelVictoria));
        if (panelDerrotaPorBoss != null) StartCoroutine(FadeOutPanel(panelDerrotaPorBoss));
        if (panelDerrotaPorSospecha != null) StartCoroutine(FadeOutPanel(panelDerrotaPorSospecha));
    }

    private void ActualizarPaneles(ResultadoPartida nuevoResultado)
    {
        AvisarSiFalta(panelVictoria != null ? panelVictoria.gameObject : null, nameof(panelVictoria));
        AvisarSiFalta(panelDerrotaPorBoss != null ? panelDerrotaPorBoss.gameObject : null, nameof(panelDerrotaPorBoss));
        AvisarSiFalta(panelDerrotaPorSospecha != null ? panelDerrotaPorSospecha.gameObject : null, nameof(panelDerrotaPorSospecha));

        CanvasGroup panelAMostrar = nuevoResultado switch
        {
            ResultadoPartida.VictoriaJugadores => panelVictoria,
            ResultadoPartida.DerrotaPorBoss => panelDerrotaPorBoss,
            ResultadoPartida.DerrotaPorSospecha => panelDerrotaPorSospecha,
            _ => null
        };

        if (panelAMostrar != null)
        {
            StartCoroutine(FadeInPanel(panelAMostrar));
        }

        Debug.Log($"[GameManager] Resultado actualizado a: {nuevoResultado}");

        if (nuevoResultado == ResultadoPartida.VictoriaJugadores && textoNombreGanador != null)
        {
            string nombre = PlayerNamePanelsUI.Instance != null
                ? PlayerNamePanelsUI.Instance.ObtenerNombrePorSlot(slotGanador.Value)
                : $"Jugador {slotGanador.Value}";

            textoNombreGanador.text = $"¡{nombre} ganó!";
        }
    }

    private IEnumerator FadeInPanel(CanvasGroup panel)
    {
        panel.gameObject.SetActive(true);
        panel.alpha = 0f;
        panel.blocksRaycasts = false;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            panel.alpha = Mathf.Lerp(0f, 1f, t / fadeDuration);
            yield return null;
        }

        panel.alpha = 1f;
        panel.blocksRaycasts = true;
    }

    private IEnumerator FadeOutPanel(CanvasGroup panel)
    {
        panel.blocksRaycasts = false;
        float alphaInicial = panel.alpha;
        float t = 0f;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            panel.alpha = Mathf.Lerp(alphaInicial, 0f, t / fadeDuration);
            yield return null;
        }

        panel.alpha = 0f;
        panel.gameObject.SetActive(false);
    }

    private void AvisarSiFalta(GameObject panel, string nombreCampo)
    {
        if (panel == null)
        {
            Debug.LogWarning($"[GameManager] El campo '{nombreCampo}' no está asignado en el Inspector - ese panel nunca se va a poder mostrar.");
        }
    }
}