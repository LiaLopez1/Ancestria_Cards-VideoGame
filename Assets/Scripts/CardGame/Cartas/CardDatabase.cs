using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tabla de consulta ID -> CardData. Existe para que, mas adelante, cuando
/// sincronicemos cartas por red, solo tengamos que mandar un numero (el
/// cardId) en vez del ScriptableObject completo (Netcode no puede
/// serializar referencias a ScriptableObject directamente).
///
/// Esto NO necesita sincronizarse por red: es configuracion fija del juego
/// (los assets de CardData), asi que ya es identica en el proyecto de todos
/// los jugadores. Cada cliente arma su propia copia de esta tabla en Awake.
/// </summary>
public class CardDatabase : MonoBehaviour
{
    public static CardDatabase Instance { get; private set; }

    [Header("Todas las cartas del juego (una copia por carta unica)")]
    [SerializeField] private List<CardData> allCards;

    private readonly Dictionary<int, CardData> cardsById = new Dictionary<int, CardData>();

    private void Awake()
    {
        Instance = this;
        ConstruirTabla();
    }

    private void ConstruirTabla()
    {
        cardsById.Clear();

        foreach (CardData carta in allCards)
        {
            if (carta == null) continue;

            if (cardsById.ContainsKey(carta.cardId))
            {
                Debug.LogError($"[CardDatabase] Hay dos cartas con el mismo cardId ({carta.cardId}): " +
                    $"'{cardsById[carta.cardId].cardName}' y '{carta.cardName}'. Los IDs deben ser unicos.");
                continue;
            }

            cardsById[carta.cardId] = carta;
        }

        Debug.Log($"[CardDatabase] Tabla construida con {cardsById.Count} carta(s).");
    }

    public CardData ObtenerPorId(int cardId)
    {
        if (cardsById.TryGetValue(cardId, out CardData carta))
        {
            return carta;
        }

        Debug.LogError($"[CardDatabase] No existe ninguna carta con cardId={cardId}.");
        return null;
    }

    public List<CardData> ObtenerTodas()
    {
        return allCards;
    }
}