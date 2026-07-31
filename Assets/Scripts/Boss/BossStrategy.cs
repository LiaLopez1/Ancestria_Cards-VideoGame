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
/// La idea general es la misma para las cuatro reglas: agrupar segun el
/// criterio que pide la regla, quedarse con el grupo mas prometedor, y
/// descartar la carta que menos aporta a ese grupo.
///
/// CartaInfiltrada / CartaInfiltrada2 agregan un paso extra: antes de
/// agrupar, se reserva (nunca se descarta) una carta que pertenezca a la
/// categoriaInfiltrada - es el "comodin" que exige la regla. El resto de
/// la mano (3 cartas) se evalua igual que las reglas base, pero apuntando
/// a un trio en vez de a las 4 cartas completas.
/// </summary>
public static class BossStrategy
{
    /// <param name="categoriaInfiltrada">
    /// Solo se usa para CartaInfiltrada / CartaInfiltrada2. Pasa null para
    /// las demas reglas.
    /// </param>
    public static int ElegirCartaADescartar(List<int> manoCardIds, VictoryRuleType reglaActiva, CardCategory? categoriaInfiltrada = null)
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

            case VictoryRuleType.CartaInfiltrada:
                return EvaluarCartaInfiltrada(manoCardIds, categoriaInfiltrada);

            case VictoryRuleType.CartaInfiltrada2:
                return EvaluarCartaInfiltrada2(manoCardIds, categoriaInfiltrada);

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
        Dictionary<CardCategory, List<int>> porCategoria = AgruparPorCategoria(mano);

        if (porCategoria.Count == 0)
        {
            return mano[0];
        }

        CardCategory mejorCategoria = EncontrarMejorCategoria(porCategoria);

        int? duplicado = BuscarDuplicadoDentroDe(porCategoria[mejorCategoria]);
        if (duplicado.HasValue)
        {
            return duplicado.Value;
        }

        int? deCategoriaMenosUtil = BuscarCartaDeCategoriaMenosUtil(porCategoria, mejorCategoria);

        return deCategoriaMenosUtil ?? mano[0];
    }

    /// <summary>
    /// Trio de 3 cartas DISTINTAS de una misma categoria + CUALQUIER carta
    /// de la categoriaInfiltrada. Reserva una carta de esa categoria como
    /// comodin (nunca se descarta mientras haya otra opcion) y evalua el
    /// resto igual que CategoriaCompleta, pero apuntando a 3, no a 4.
    /// </summary>
    private static int EvaluarCartaInfiltrada(List<int> mano, CardCategory? categoriaInfiltrada)
    {
        // Sin categoria infiltrada valida no hay nada especial que
        // proteger - se aproxima con la misma logica que CategoriaCompleta.
        if (categoriaInfiltrada == null)
        {
            return EvaluarCategoriaCompleta(mano);
        }

        int comodinId = BuscarComodin(mano, categoriaInfiltrada.Value);
        List<int> resto = QuitarComodin(mano, comodinId);

        if (comodinId == -1)
        {
            // No hay ninguna carta de la categoria infiltrada en mano
            // todavia - no hay comodin que proteger, se evalua la mano
            // completa buscando la mejor agrupacion posible mientras tanto.
            return EvaluarCategoriaCompleta(mano);
        }

        Dictionary<CardCategory, List<int>> porCategoria = AgruparPorCategoria(resto);

        if (porCategoria.Count == 0)
        {
            return resto.Count > 0 ? resto[0] : comodinId;
        }

        CardCategory mejorCategoria = EncontrarMejorCategoria(porCategoria);

        int? duplicado = BuscarDuplicadoDentroDe(porCategoria[mejorCategoria]);
        if (duplicado.HasValue)
        {
            return duplicado.Value;
        }

        int? deCategoriaMenosUtil = BuscarCartaDeCategoriaMenosUtil(porCategoria, mejorCategoria);

        return deCategoriaMenosUtil ?? resto[0];
    }

    /// <summary>
    /// 3 copias IDENTICAS + CUALQUIER carta de la categoriaInfiltrada.
    /// Mismo comodin que CartaInfiltrada, pero el resto se evalua buscando
    /// el cardId mas repetido (como CuatroIguales, apuntando a 3).
    /// </summary>
    private static int EvaluarCartaInfiltrada2(List<int> mano, CardCategory? categoriaInfiltrada)
    {
        if (categoriaInfiltrada == null)
        {
            return EvaluarCuatroIguales(mano);
        }

        int comodinId = BuscarComodin(mano, categoriaInfiltrada.Value);
        List<int> resto = QuitarComodin(mano, comodinId);

        if (comodinId == -1)
        {
            return EvaluarCuatroIguales(mano);
        }

        if (resto.Count == 0)
        {
            return comodinId; // no deberia pasar con una mano de 4 cartas
        }

        Dictionary<int, int> conteo = new Dictionary<int, int>();

        foreach (int id in resto)
        {
            conteo[id] = conteo.TryGetValue(id, out int actual) ? actual + 1 : 1;
        }

        int mejorCardId = resto[0];
        int mejorConteo = 0;

        foreach (KeyValuePair<int, int> par in conteo)
        {
            if (par.Value > mejorConteo)
            {
                mejorConteo = par.Value;
                mejorCardId = par.Key;
            }
        }

        int cardIdADescartar = -1;
        int menorConteo = int.MaxValue;

        foreach (int id in resto)
        {
            if (id == mejorCardId)
            {
                continue;
            }

            int conteoDeEsta = conteo[id];

            if (conteoDeEsta < menorConteo)
            {
                menorConteo = conteoDeEsta;
                cardIdADescartar = id;
            }
        }

        return cardIdADescartar != -1 ? cardIdADescartar : resto[0];
    }

    // ---------------- Helpers compartidos ----------------

    private static Dictionary<CardCategory, List<int>> AgruparPorCategoria(List<int> cardIds)
    {
        Dictionary<CardCategory, List<int>> porCategoria = new Dictionary<CardCategory, List<int>>();

        foreach (int id in cardIds)
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

        return porCategoria;
    }

    private static CardCategory EncontrarMejorCategoria(Dictionary<CardCategory, List<int>> porCategoria)
    {
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

        return mejorCategoria;
    }

    /// <summary>Busca un cardId repetido dentro del grupo dado. Null si no hay ninguno.</summary>
    private static int? BuscarDuplicadoDentroDe(List<int> grupo)
    {
        Dictionary<int, int> conteo = new Dictionary<int, int>();

        foreach (int id in grupo)
        {
            conteo[id] = conteo.TryGetValue(id, out int actual) ? actual + 1 : 1;
        }

        foreach (KeyValuePair<int, int> par in conteo)
        {
            if (par.Value > 1)
            {
                return par.Key;
            }
        }

        return null;
    }

    /// <summary>Entre las categorias que NO son la mejor, devuelve una carta de la menos util. Null si no hay ninguna otra categoria.</summary>
    private static int? BuscarCartaDeCategoriaMenosUtil(Dictionary<CardCategory, List<int>> porCategoria, CardCategory mejorCategoria)
    {
        CardCategory categoriaMenosUtil = default;
        int menorCantidadDistintos = int.MaxValue;
        bool encontro = false;

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
                encontro = true;
            }
        }

        return encontro ? porCategoria[categoriaMenosUtil][0] : (int?)null;
    }

    /// <summary>Primera carta de la mano que pertenece a la categoria infiltrada. -1 si no hay ninguna.</summary>
    private static int BuscarComodin(List<int> mano, CardCategory categoriaInfiltrada)
    {
        foreach (int id in mano)
        {
            CardData carta = CardDatabase.Instance.ObtenerPorId(id);

            if (carta != null && carta.category == categoriaInfiltrada)
            {
                return id;
            }
        }

        return -1;
    }

    private static List<int> QuitarComodin(List<int> mano, int comodinId)
    {
        List<int> resto = new List<int>(mano);

        if (comodinId != -1)
        {
            resto.Remove(comodinId); // solo quita UNA ocurrencia
        }

        return resto;
    }

    // ---------------- Tension: "¿le falta una sola carta para ganar?" ----------------

    /// <summary>
    /// Evalua la mano FINAL del boss (4 cartas, ya despues de descartar) y
    /// devuelve true si le alcanzaria con UNA carta mas (la correcta) para
    /// cumplir la regla activa. Pensado para disparar una alerta de tension
    /// a los jugadores - no afecta ninguna decision del boss, solo informa.
    /// </summary>
    public static bool EstaAUnaCartaDeGanar(List<int> manoFinal, VictoryRuleType reglaActiva, CardCategory? categoriaInfiltrada = null)
    {
        if (manoFinal == null || manoFinal.Count != 4)
        {
            return false;
        }

        switch (reglaActiva)
        {
            case VictoryRuleType.CuatroIguales:
                return AUnaCartaDeGanarCuatroIguales(manoFinal);

            case VictoryRuleType.CategoriaCompleta:
                return AUnaCartaDeGanarCategoriaCompleta(manoFinal);

            case VictoryRuleType.CartaInfiltrada:
                return AUnaCartaDeGanarCartaInfiltrada(manoFinal, categoriaInfiltrada);

            case VictoryRuleType.CartaInfiltrada2:
                return AUnaCartaDeGanarCartaInfiltrada2(manoFinal, categoriaInfiltrada);

            default:
                return false;
        }
    }

    /// <summary>True si algun cardId aparece exactamente 3 veces (le falta 1 copia mas para las 4 iguales).</summary>
    private static bool AUnaCartaDeGanarCuatroIguales(List<int> mano)
    {
        foreach (KeyValuePair<int, int> par in ContarPorCardId(mano))
        {
            if (par.Value == 3)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True si alguna categoria tiene exactamente 3 cardIds DISTINTOS (le falta 1 mas para completarla).</summary>
    private static bool AUnaCartaDeGanarCategoriaCompleta(List<int> mano)
    {
        foreach (KeyValuePair<CardCategory, List<int>> par in AgruparPorCategoria(mano))
        {
            if (new HashSet<int>(par.Value).Count == 3)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Ya tiene el comodin (carta de la categoria infiltrada) reservado -
    /// true si, entre las 3 cartas restantes, hay 2 DISTINTAS de una misma
    /// categoria (le falta 1 mas para el trio completo).
    /// </summary>
    private static bool AUnaCartaDeGanarCartaInfiltrada(List<int> mano, CardCategory? categoriaInfiltrada)
    {
        if (categoriaInfiltrada == null)
        {
            return false;
        }

        int comodinId = BuscarComodin(mano, categoriaInfiltrada.Value);

        if (comodinId == -1)
        {
            return false; // sin comodin todavia, esta a mas de una carta
        }

        List<int> resto = QuitarComodin(mano, comodinId);

        foreach (KeyValuePair<CardCategory, List<int>> par in AgruparPorCategoria(resto))
        {
            if (new HashSet<int>(par.Value).Count == 2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Ya tiene el comodin reservado - true si, entre las 3 cartas
    /// restantes, hay 2 copias IDENTICAS del mismo cardId (le falta 1 mas
    /// para las 3 copias completas).
    /// </summary>
    private static bool AUnaCartaDeGanarCartaInfiltrada2(List<int> mano, CardCategory? categoriaInfiltrada)
    {
        if (categoriaInfiltrada == null)
        {
            return false;
        }

        int comodinId = BuscarComodin(mano, categoriaInfiltrada.Value);

        if (comodinId == -1)
        {
            return false;
        }

        List<int> resto = QuitarComodin(mano, comodinId);

        foreach (KeyValuePair<int, int> par in ContarPorCardId(resto))
        {
            if (par.Value == 2)
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<int, int> ContarPorCardId(List<int> cardIds)
    {
        Dictionary<int, int> conteo = new Dictionary<int, int>();

        foreach (int id in cardIds)
        {
            conteo[id] = conteo.TryGetValue(id, out int actual) ? actual + 1 : 1;
        }

        return conteo;
    }
}