using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Parte "bots" de DeckManager, en un archivo aparte para no tocar el grande.
/// Funciona porque DeckManager ahora es 'partial': los dos archivos son la
/// MISMA clase, así que este puede usar sus miembros privados (manoPorCliente,
/// discardPile, DrawCard(), turnManager, gameManager...).
///
/// Mismo patrón que los métodos del boss (RobarCartaParaBoss /
/// DescartarCartaDelBoss): corren SOLO en el servidor, sin ServerRpc, y
/// reutilizan la misma mano por cliente, la misma pila de descarte y el
/// mismo aviso a todos (MostrarCartaDescartadaClientRpc). La diferencia con
/// el boss es que un bot SÍ ocupa un slot de jugador, así que valida su
/// turno con ese slot y, si gana, declara victoria de JUGADORES.
/// </summary>
public partial class DeckManager
{
    private readonly Dictionary<ulong, Coroutine> corrutinasDeRepartoBots = new Dictionary<ulong, Coroutine>();

    /// <summary>0 en partidas online; BotIds.Cantidad en modo local con bots.</summary>
    public int CantidadDeBotsActivos()
    {
        return NetworkBootstrap.ModoLocalConBots ? BotIds.Cantidad : 0;
    }

    /// <summary>
    /// SOLO servidor. Reparte la mano inicial a cada bot (sin ClientRpc:
    /// nadie ve la mano de un bot). Se llama justo después del reparto a los
    /// jugadores reales, desde ContinuarResetDespuesDelBarajado().
    /// </summary>
    public void IniciarRepartoDeBots()
    {
        if (!IsServer || CantidadDeBotsActivos() == 0)
        {
            return;
        }

        for (int i = 0; i < BotIds.Cantidad; i++)
        {
            ulong botId = BotIds.IdDeSlot(BotIds.PrimerSlot + i);

            if (corrutinasDeRepartoBots.TryGetValue(botId, out Coroutine vieja) && vieja != null)
            {
                StopCoroutine(vieja);
            }

            corrutinasDeRepartoBots[botId] = StartCoroutine(RepartirManoABot(botId));
        }
    }

    private IEnumerator RepartirManoABot(ulong botId)
    {
        for (int i = 0; i < initialHandSize; i++)
        {
            yield return new WaitForSeconds(delayBetweenCards);

            CardData carta = DrawCard();

            if (carta == null)
            {
                yield break;
            }

            AgregarCartaAManoDeCliente(botId, carta.cardId);
        }

        Debug.Log($"[Servidor] Reparto inicial terminado para el bot {botId}.");
    }

    /// <summary>Mano actual de un bot (solo cardIds). Es la lista real: no modificarla desde fuera.</summary>
    public List<int> ObtenerManoDeBot(ulong botId)
    {
        return ObtenerManoDeCliente(botId);
    }

    /// <summary>
    /// SOLO servidor. El bot roba en SU turno. Devuelve null si no le toca,
    /// si ya robó (no tiene 4 cartas) o si no quedan cartas. Avisa a
    /// TurnManager para pasar a "esperando descarte", igual que un robo real.
    /// </summary>
    public CardData RobarCartaParaBot(ulong botId, int slot)
    {
        if (!IsServer || turnManager == null || !turnManager.EsTurnoDelSlot(slot))
        {
            return null;
        }

        if (ObtenerManoDeCliente(botId).Count != initialHandSize)
        {
            return null;
        }

        CardData carta = DrawCard();

        if (carta == null)
        {
            return null;
        }

        AgregarCartaAManoDeCliente(botId, carta.cardId);
        turnManager.NotificarRoboRealizado();

        return carta;
    }

    /// <summary>
    /// SOLO servidor. El bot descarta una carta puntual. Mismo camino que
    /// SolicitarDescarteServerRpc: suma al descarte, avisa a todos, revisa
    /// la regla de victoria (si cumple, ganan los JUGADORES, no el boss) y
    /// avanza el turno. Devuelve false si la acción no era válida.
    /// </summary>
    public bool DescartarCartaDeBot(ulong botId, int slot, int cardId)
    {
        if (!IsServer || turnManager == null || !turnManager.EsTurnoDelSlot(slot))
        {
            return false;
        }

        List<int> mano = ObtenerManoDeCliente(botId);

        if (mano.Count != initialHandSize + 1 || !mano.Remove(cardId))
        {
            Debug.LogWarning($"[Servidor] El bot del slot {slot} intentó descartar una carta inválida (cardId={cardId}).");
            return false;
        }

        CardData cartaDescartada = CardDatabase.Instance.ObtenerPorId(cardId);

        if (cartaDescartada != null)
        {
            discardPile.Add(cartaDescartada);
        }
        else
        {
            Debug.LogError($"[Servidor] cardId inválido al descartar (bot): {cardId}");
        }

        Debug.Log($"[Servidor] El bot del slot {slot} descartó cardId={cardId}. Le quedan {mano.Count} carta(s).");

        MostrarCartaDescartadaClientRpc(cardId);

        if (VictoryRules.SeCumple(turnManager.ReglaActiva, mano, ObtenerCategoriaInfiltradaActual()))
        {
            if (gameManager != null)
            {
                gameManager.DeclararVictoriaJugador(slot);
            }
            else
            {
                Debug.LogError("[DeckManager] Un bot cumplió la regla de victoria, pero no se asignó GameManager.");
            }

            return true; // no avanzamos el turno, la ronda ya terminó
        }

        turnManager.NotificarDescarteRealizado();
        return true;
    }
}