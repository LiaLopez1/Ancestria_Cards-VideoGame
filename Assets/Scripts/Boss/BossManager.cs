using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orquesta al boss como "un jugador más": recibe mano inicial del mismo
/// mazo compartido, la muestra boca abajo (BossHandUI), y solo al descartar
/// crea una carta informativa nueva (con CardData real) directo en la mesa -
/// nunca existe una versión "legible" de la carta mientras está en su mano.
///
/// PROTOTIPO: sigue sin red. El turno del boss se dispara automático por el
/// evento OnPlayerTurnFinished de TurnManager.
/// </summary>
public class BossManager : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private DeckManager deckManager;
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private BossHandUI bossHandUI;
    [SerializeField] private TableManager tableManager;

    [Tooltip("El prefab CON información (el mismo que usa HandManager para el jugador) - se instancia recién al descartar, nunca en la mano.")]
    [SerializeField] private GameObject cartaInformativaPrefab;

    [Header("Ritmo del boss")]
    [Tooltip("Espera artificial antes de robar/descartar en su turno. También sirve como perilla de dificultad.")]
    [SerializeField] private float delayAntesDeActuar = 1.5f;
    [Tooltip("Espera entre cada carta durante el reparto inicial (mismo propósito que delayBetweenCards en DeckManager).")]
    [SerializeField] private float delayEntreCartasReparto = 0.25f;

    // Mano lógica del boss. bossHand y bossVisualCards se mantienen SIEMPRE
    // en el mismo índice (la carta lógica N corresponde a la carta oculta
    // visual N) - así, al descartar por índice, sabemos qué GameObject
    // boca abajo hay que destruir.
    private readonly List<CardData> bossHand = new List<CardData>();
    private readonly List<GameObject> bossVisualCards = new List<GameObject>();

    public int CartasEnMano => bossHand.Count;

    private void OnEnable()
    {
        if (turnManager != null)
        {
            turnManager.OnPlayerTurnFinished += JugarTurno;
        }
    }

    private void OnDisable()
    {
        if (turnManager != null)
        {
            turnManager.OnPlayerTurnFinished -= JugarTurno;
        }
    }

    private void Start()
    {
        StartCoroutine(RepartirManoInicial());
    }

    /// <summary>
    /// Igual que DealInitialHand() en DeckManager, pero para el boss. Espera
    /// a que el reparto del jugador termine (TurnManager llega a
    /// WaitingToDraw) para no pisarse con esa corrutina, y roba del mismo
    /// drawPile compartido - por eso 4 cartas más se descuentan del mazo.
    /// </summary>
    private IEnumerator RepartirManoInicial()
    {
        yield return new WaitUntil(() => turnManager.CurrentState == TurnState.WaitingToDraw);

        int cantidad = deckManager.InitialHandSize;

        for (int i = 0; i < cantidad; i++)
        {
            yield return new WaitForSeconds(delayEntreCartasReparto);

            CardData cartaRepartida = deckManager.DrawCard();

            if (cartaRepartida == null)
            {
                Debug.LogWarning("[Boss] El mazo se quedó sin cartas durante el reparto inicial.");
                yield break;
            }

            AgregarCartaAMano(cartaRepartida);
        }

        Debug.Log("[Boss] Reparto inicial terminado. Tiene " + bossHand.Count + " cartas.");
    }

    private void AgregarCartaAMano(CardData cardData)
    {
        bossHand.Add(cardData);

        // Solo se crea la carta OCULTA (boca abajo) - jamás se revela el
        // CardData real mientras está en la mano.
        GameObject visualOculta = bossHandUI != null ? bossHandUI.InstanciarCartaOculta() : null;
        bossVisualCards.Add(visualOculta); // puede quedar null si falta bossHandUI - lo toleramos en el prototipo

        Debug.Log("[Boss] Recibió una carta (oculta). Mano actual: " + bossHand.Count + " cartas.");
    }

    /// <summary>
    /// Punto de entrada del turno del boss. Se dispara automáticamente
    /// cuando el jugador termina su turno (evento OnPlayerTurnFinished),
    /// pero el ContextMenu se deja para poder probarlo a mano igual.
    /// </summary>
    [ContextMenu("Probar turno del boss")]
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

        turnManager.StartBossTurn();
        Debug.Log("[Boss] Empieza su turno.");

        yield return new WaitForSeconds(delayAntesDeActuar);

        // --- Robar ---
        CardData cartaRobada = deckManager.DrawCard();

        if (cartaRobada == null)
        {
            Debug.LogWarning("[Boss] No pudo robar (mazo vacío). Termina el turno sin descartar.");
            turnManager.EndBossTurn();
            yield break;
        }

        AgregarCartaAMano(cartaRobada);
        turnManager.BossCardWasDrawn();

        yield return new WaitForSeconds(delayAntesDeActuar);

        // --- Evaluar y descartar ---
        int indiceADescartar = BossStrategy.ElegirCartaADescartar(bossHand);

        if (indiceADescartar < 0 || indiceADescartar >= bossHand.Count)
        {
            Debug.LogError("[Boss] BossStrategy devolvió un índice inválido.");
            yield break;
        }

        CardData cartaDescartada = bossHand[indiceADescartar];
        GameObject visualOcultaADescartar = bossVisualCards[indiceADescartar];

        bossHand.RemoveAt(indiceADescartar);
        bossVisualCards.RemoveAt(indiceADescartar);

        Debug.Log("[Boss] Descartó: " + cartaDescartada.cardName + " | Mano restante: " + bossHand.Count + " cartas.");

        // La carta oculta se destruye - ya cumplió su propósito (mostrar que
        // el boss tenía una carta más, sin revelar cuál).
        if (bossHandUI != null && visualOcultaADescartar != null)
        {
            bossHandUI.DescartarCartaOculta(visualOcultaADescartar);
        }

        // Recién ACÁ se crea la carta real (informativa), directo en la mesa -
        // es la primera vez que existe una versión "legible" de esta carta.
        CrearCartaInformativaEnMesa(cartaDescartada);

        turnManager.BossCardWasDiscarded();

        // --- Devolver el turno ---
        turnManager.EndBossTurn();
        Debug.Log("[Boss] Termina su turno.");
    }

    private void CrearCartaInformativaEnMesa(CardData cardData)
    {
        if (cartaInformativaPrefab == null || tableManager == null || bossHandUI == null)
        {
            Debug.LogWarning("[Boss] No se pudo crear la carta informativa (faltan referencias).");
            return;
        }

        // Se instancia temporalmente bajo el área de la mano del boss (un
        // RectTransform que ya sabemos que está bajo el Canvas correcto) -
        // TableManager.PlaceCard() la va a reparentar a la mesa de inmediato,
        // así que el padre inicial no importa más que estar en el Canvas.
        GameObject cartaEnMesa = Instantiate(cartaInformativaPrefab, bossHandUI.BossHandArea);
        cartaEnMesa.name = "BossCard descartada - " + cardData.cardName;

        CardDisplay display = cartaEnMesa.GetComponent<CardDisplay>();

        if (display != null)
        {
            display.card = cardData;
        }
        else
        {
            Debug.LogWarning("[Boss] El prefab informativo no tiene CardDisplay.");
        }

        // No debe poder arrastrarse desde la mesa.
        CardDragHandler drag = cartaEnMesa.GetComponent<CardDragHandler>();

        if (drag != null)
        {
            drag.enabled = false;
        }

        RectTransform cardRect = cartaEnMesa.GetComponent<RectTransform>();

        if (cardRect == null)
        {
            Debug.LogError("[Boss] El prefab informativo no tiene RectTransform.");
            return;
        }

        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);

        tableManager.PlaceCard(cardRect);
    }
}