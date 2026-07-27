using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Representación visual de la mano del boss - SIEMPRE boca abajo.
///
/// A propósito usa un prefab distinto al de la mano del jugador: el mismo
/// "reverso" que ya usa DeckManager para el mazo (deckCardPrefab), sin
/// CardData ni CardDisplay. La carta informativa real (con nombre/ícono) se
/// crea en otro lado - recién en el momento de descartar, directo en la mesa
/// (ver BossManager) - así nunca existe una versión "informativa" de la
/// carta mientras está en la mano del boss.
///
/// Acomodo manual (sin Layout Group), mismo patrón que HandManager.ArrangeHand().
/// </summary>
public class BossHandUI : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Si se deja vacío, se usa el RectTransform de este mismo GameObject.")]
    [SerializeField] private RectTransform bossHandArea;

    [Tooltip("El prefab boca abajo (el mismo que usa DeckManager para el mazo). NUNCA el prefab informativo del jugador.")]
    [SerializeField] private GameObject cardBackPrefab;

    [Header("Acomodo (manual)")]
    [SerializeField] private float spacing = 120f;

    // Cartas actualmente visibles en la mano - se usa solo para recalcular
    // las posiciones cuando se agrega o quita una.
    private readonly List<RectTransform> cartasVisibles = new List<RectTransform>();

    public RectTransform BossHandArea => bossHandArea;

    private void Awake()
    {
        if (bossHandArea == null)
        {
            bossHandArea = GetComponent<RectTransform>();
        }
    }

    /// <summary>
    /// Instancia una carta boca abajo genérica en la mano del boss. No recibe
    /// ni usa ningún CardData - es puramente decorativa, indica "el boss
    /// tiene una carta más" sin revelar cuál.
    /// </summary>
    public GameObject InstanciarCartaOculta()
    {
        if (cardBackPrefab == null || bossHandArea == null)
        {
            Debug.LogError("[BossHandUI] Faltan referencias (cardBackPrefab o bossHandArea).");
            return null;
        }

        GameObject nuevaCarta = Instantiate(cardBackPrefab, bossHandArea);
        nuevaCarta.name = "BossCard (oculta)";

        // Por si el prefab del mazo trae DeckCardDrag u otro componente de
        // arrastre - las cartas del boss nunca deben ser interactivas.
        DeckCardDrag deckDrag = nuevaCarta.GetComponent<DeckCardDrag>();
        if (deckDrag != null)
        {
            deckDrag.enabled = false;
        }

        CardDragHandler drag = nuevaCarta.GetComponent<CardDragHandler>();
        if (drag != null)
        {
            drag.enabled = false;
        }

        RectTransform cardRect = nuevaCarta.GetComponent<RectTransform>();

        if (cardRect != null)
        {
            cardRect.localScale = Vector3.one;
            cardRect.localRotation = Quaternion.identity;
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);

            cartasVisibles.Add(cardRect);
        }

        Reacomodar();

        return nuevaCarta;
    }

    /// <summary>
    /// Se llama cuando una carta oculta deja la mano (el boss la va a
    /// descartar). A diferencia de la versión anterior, esta SÍ destruye el
    /// GameObject - ya no se reutiliza para la mesa, porque la carta que va
    /// a la mesa es una informativa nueva, creada aparte por BossManager.
    /// </summary>
    public void DescartarCartaOculta(GameObject cartaOculta)
    {
        if (cartaOculta == null)
        {
            return;
        }

        RectTransform cardRect = cartaOculta.GetComponent<RectTransform>();

        if (cardRect != null)
        {
            cartasVisibles.Remove(cardRect);
        }

        Destroy(cartaOculta);
        Reacomodar();
    }

    private void Reacomodar()
    {
        int count = cartasVisibles.Count;

        if (count == 0)
        {
            return;
        }

        float centro = (count - 1) / 2f;

        for (int i = 0; i < count; i++)
        {
            float offset = i - centro;
            cartasVisibles[i].anchoredPosition = new Vector2(offset * spacing, 0f);
            cartasVisibles[i].SetSiblingIndex(i);
        }
    }
}