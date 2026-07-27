using System.Collections.Generic;

/// <summary>
/// Cerebro del boss: decide qué carta descartar según la mano actual y la
/// regla de victoria activa de la ronda.
///
/// La mano se recibe como List<int> (cardIds), igual que VictoryRules - la
/// identidad real de las cartas se resuelve puntualmente via
/// CardDatabase.Instance.ObtenerPorId(id) solo cuando hace falta (para
/// CategoriaCompleta, que necesita saber la categoria de cada carta).
///
/// La idea general es la misma para las dos reglas: agrupar segun el
/// criterio que pide la regla, quedarse con el grupo mas prometedor, y
/// descartar la carta que menos aporta a ese grupo.
/// </summary>
public static class BossStrategy
{
    public static int ElegirCartaADescartar(List<int> manoCardIds, VictoryRuleType reglaActiva)
    {
        if (manoCardIds == null || manoCardIds.Count == 0)
        {
            return -1;
        }

        switch (reglaActiva)
        {
            case VictoryRuleType.CuatroIguales:
                return EvaluarCuatroIguales(manoCardIds);

            case VictoryRuleType.CategoriaCompleta:
                return EvaluarCategoriaCompleta(manoCardIds);

            default:
                return manoCardIds[0];
        }
    }

    /// <summary>
    /// Agrupa por cardId. Conserva el grupo mas grande (el mas cerca de
    /// completar "4 iguales"), y descarta la carta mas aislada fuera de ese
    /// grupo - es decir, la que pertenece al grupo mas chico entre las que
    /// no son del mejor cardId.
    /// </summary>
    private static int EvaluarCuatroIguales(List<int> mano)
    {
        Dictionary<int, int> conteoPorCardId = new Dictionary<int, int>();

        foreach (int id in mano)
        {
            conteoPorCardId[id] = conteoPorCardId.TryGetValue(id, out int actual) ? actual + 1 : 1;
        }

        int mejorCardId = mano[0];
        int mejorConteo = 0;

        foreach (KeyValuePair<int, int> par in conteoPorCardId)
        {
            if (par.Value > mejorConteo)
            {
                mejorConteo = par.Value;
                mejorCardId = par.Key;
            }
        }

        int cardIdADescartar = -1;
        int menorConteo = int.MaxValue;

        foreach (int id in mano)
        {
            if (id == mejorCardId)
            {
                continue;
            }

            int conteoDeEsta = conteoPorCardId[id];

            if (conteoDeEsta < menorConteo)
            {
                menorConteo = conteoDeEsta;
                cardIdADescartar = id;
            }
        }

        // Si TODA la mano es del mismo cardId (caso raro/ya ganador), no hay
        // nada "afuera" para descartar - por defecto, la primera.
        return cardIdADescartar != -1 ? cardIdADescartar : mano[0];
    }

    /// <summary>
    /// Agrupa por categoria (resolviendo CardData por cardId). Conserva la
    /// categoria con mas cardIds DISTINTOS. Prioridad de descarte:
    /// 1) un cardId duplicado dentro de la mejor categoria (no aporta, la
    ///    regla exige 4 distintos, no 4 copias).
    /// 2) si no hay duplicados, una carta de la categoria menos prometedora.
    /// </summary>
    private static int EvaluarCategoriaCompleta(List<int> mano)
    {
        Dictionary<CardCategory, List<int>> porCategoria = new Dictionary<CardCategory, List<int>>();

        foreach (int id in mano)
        {
            CardData carta = CardDatabase.Instance.ObtenerPorId(id);

            if (carta == null)
            {
                continue;
            }

            if (!porCategoria.TryGetValue(carta.category, out List<int> idsDeEstaCategoria))
            {
                idsDeEstaCategoria = new List<int>();
                porCategoria[carta.category] = idsDeEstaCategoria;
            }

            idsDeEstaCategoria.Add(id);
        }

        if (porCategoria.Count == 0)
        {
            return mano[0];
        }

        CardCategory mejorCategoria = default;
        int mejorCantidadDistintos = -1;

        foreach (KeyValuePair<CardCategory, List<int>> par in porCategoria)
        {
            int distintos = new HashSet<int>(par.Value).Count;

            if (distintos > mejorCantidadDistintos)
            {
                mejorCantidadDistintos = distintos;
                mejorCategoria = par.Key;
            }
        }

        // Prioridad 1: duplicado dentro de la mejor categoria.
        Dictionary<int, int> conteoDentroDeLaMejor = new Dictionary<int, int>();

        foreach (int id in porCategoria[mejorCategoria])
        {
            conteoDentroDeLaMejor[id] = conteoDentroDeLaMejor.TryGetValue(id, out int actual) ? actual + 1 : 1;
        }

        foreach (KeyValuePair<int, int> par in conteoDentroDeLaMejor)
        {
            if (par.Value > 1)
            {
                return par.Key; // hay una copia de mas de este cardId - se descarta una
            }
        }

        // Prioridad 2: sin duplicados - descartar de la categoria menos prometedora.
        CardCategory categoriaMenosUtil = default;
        int menorCantidadDistintos = int.MaxValue;
        bool encontroCategoriaAfuera = false;

        foreach (KeyValuePair<CardCategory, List<int>> par in porCategoria)
        {
            if (par.Key.Equals(mejorCategoria))
            {
                continue;
            }

            int distintos = new HashSet<int>(par.Value).Count;

            if (distintos < menorCantidadDistintos)
            {
                menorCantidadDistintos = distintos;
                categoriaMenosUtil = par.Key;
                encontroCategoriaAfuera = true;
            }
        }

        if (encontroCategoriaAfuera)
        {
            return porCategoria[categoriaMenosUtil][0];
        }

        // Fallback: toda la mano es de la misma categoria y sin duplicados
        // (mano ya ideal o caso raro) - se descarta la primera.
        return mano[0];
    }
}