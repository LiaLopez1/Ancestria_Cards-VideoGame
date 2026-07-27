using System.Collections.Generic;

/// <summary>
/// Cerebro del boss: decide qué carta descartar según la mano actual.
///
/// La mano se recibe como List<int> (cardIds), NO como List<CardData> - la
/// misma convención que ya usa VictoryRules.SeCumple(), porque en esta
/// arquitectura de red la mano real de cualquier jugador (o del boss) vive
/// como IDs, nunca como referencias directas a ScriptableObject.
///
/// PROTOTIPO: por ahora no analiza nada, solo devuelve el cardId de la
/// primera carta de la mano. El objetivo de esta etapa es dejar el flujo
/// completo funcionando (robar -> evaluar -> descartar) antes de meter la
/// heurística real por regla de victoria (4 iguales / categoría completa).
/// </summary>
public static class BossStrategy
{
    /// <summary>
    /// Devuelve el CARDID (no el índice) de la carta que el boss debería
    /// descartar. Se recibe la mano completa (incluye la carta recién
    /// robada) para que la heurística real pueda comparar todas las cartas
    /// entre sí antes de decidir.
    /// </summary>
    public static int ElegirCartaADescartar(List<int> manoCardIds, VictoryRuleType reglaActiva)
    {
        if (manoCardIds == null || manoCardIds.Count == 0)
        {
            return -1;
        }

        // TODO: reemplazar por la heurística real según reglaActiva
        // (agrupar por cardId para CuatroIguales, por category para CategoriaCompleta -
        // resolviendo CardData vía CardDatabase.Instance.ObtenerPorId(id), igual que
        // hace VictoryRules.EvaluarCategoriaCompleta()).
        return manoCardIds[0];
    }
}