using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// El boss como "un jugador más" dentro de la arquitectura de red real:
/// mismo mazo compartido, misma mano por cliente (con una identidad
/// reservada, BOSS_ID, dentro de DeckManager), mismo turno (un slot más en
/// la rotación de TurnManager). Corre ÚNICAMENTE en el servidor - nunca
/// existe una versión "de cliente" de este script haciendo nada.
///
/// Sin representación visual de su mano por ahora (ningún jugador ve la
/// mano de otro en esta arquitectura, así que el boss tampoco necesita esa
/// puesta en escena) - su descarte sí aparece en la mesa de todos, porque
/// usa el mismo ClientRpc público que ya usan los descartes de jugadores.
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

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            return; // el boss no existe del lado del cliente, ni siquiera escucha el evento
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
        }
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

        Debug.Log("[Boss] Termina su turno.");
    }
}
