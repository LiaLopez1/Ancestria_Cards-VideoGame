using System.Collections.Generic;

/// <summary>
/// Cerebro del boss: decide qué carta descartar según la mano actual.
///
/// PROTOTIPO: por ahora no analiza nada, solo devuelve el índice 0 (siempre
/// descarta la primera carta de la mano). El objetivo de esta etapa es probar
/// el flujo completo de turno (robar -> evaluar -> descartar) antes de meter
/// la heurística real por regla de victoria (4 iguales / categoría completa).
/// </summary>
public static class BossStrategy
{
    /// <summary>
    /// Devuelve el ÍNDICE (dentro de la lista "mano") de la carta que el boss
    /// debería descartar. Se recibe la mano completa (incluye la carta recién
    /// robada) para que, más adelante, la heurística real pueda comparar todas
    /// las cartas entre sí antes de decidir.
    /// </summary>
    public static int ElegirCartaADescartar(List<CardData> mano)
    {
        if (mano == null || mano.Count == 0)
        {
            return -1;
        }

        // TODO: reemplazar por la heurística real según VictoryRuleType
        // (agrupar por cardId para "4 iguales", por category para "categoría completa").
        return 0;
    }
}