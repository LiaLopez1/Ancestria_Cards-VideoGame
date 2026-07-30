using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Todas las reglas de victoria posibles. Para agregar una nueva:
/// 1) Sumar un valor aqui.
/// 2) Agregarlo al array ReglasDisponibles.
/// 3) Agregar su caso en SeCumple() (y opcionalmente en ObtenerNombre()).
/// Nada mas necesita cambiar - ni TurnManager ni DeckManager conocen los
/// detalles de cada regla, solo llaman a VictoryRules.SeCumple(...).
///
/// EXCEPCION: CartaInfiltrada y CartaInfiltrada2 SI necesitan un dato extra
/// que no vive en la mano - la carta que el Boss elige al inicio de la
/// ronda. Por eso SeCumple() ahora recibe un parametro opcional
/// cartaInfiltradaId. Quien llame a SeCumple() para estas dos reglas DEBE
/// pasarlo; si no, el parametro por defecto (-1) hace que la regla nunca
/// se cumpla (no rompe nada, simplemente queda inactiva en silencio).
/// </summary>
public enum VictoryRuleType
{
    CuatroIguales,
    CategoriaCompleta,
    CartaInfiltrada,
    CartaInfiltrada2,
}

public static class VictoryRules
{
    /// <summary>
    /// Las reglas que de verdad estan activas para sortear en una partida.
    /// Es un array a proposito: agregar una regla nueva es agregarla aqui,
    /// nada mas.
    ///
    /// CartaInfiltrada y CartaInfiltrada2 quedan comentadas por ahora -
    /// activarlas antes de que exista el codigo que elige la carta del
    /// Boss al inicio de ronda hace que esas reglas jamas se puedan
    /// cumplir (silenciosamente), lo cual confunde mas que ayuda.
    /// </summary>
    public static readonly VictoryRuleType[] ReglasDisponibles =
    {
        VictoryRuleType.CuatroIguales,
        VictoryRuleType.CategoriaCompleta,
        // VictoryRuleType.CartaInfiltrada,
        // VictoryRuleType.CartaInfiltrada2,
    };

    public static string ObtenerNombre(VictoryRuleType regla)
    {
        switch (regla)
        {
            case VictoryRuleType.CuatroIguales:
                return "4 iguales";
            case VictoryRuleType.CategoriaCompleta:
                return "Categoría completa";
            case VictoryRuleType.CartaInfiltrada:
                return "Carta infiltrada";
            case VictoryRuleType.CartaInfiltrada2:
                return "Carta infiltrada II";
            default:
                return regla.ToString();
        }
    }

    /// <summary>
    /// Evalua si una mano (lista de cardId) cumple la regla dada.
    /// Solo tiene sentido llamarlo del lado del servidor, ya que necesita
    /// la mano REAL del jugador (privada).
    /// </summary>
    /// <param name="cartaInfiltradaId">
    /// Solo se usa para CartaInfiltrada / CartaInfiltrada2 - la carta que
    /// el Boss elige al inicio de la ronda. Pasa -1 (o nada) para las
    /// demas reglas, no hace falta.
    /// </param>
    public static bool SeCumple(VictoryRuleType regla, List<int> manoCardIds, int cartaInfiltradaId = -1)
    {
        if (manoCardIds == null || manoCardIds.Count != 4)
        {
            return false;
        }

        switch (regla)
        {
            case VictoryRuleType.CuatroIguales:
                return EvaluarCuatroIguales(manoCardIds);

            case VictoryRuleType.CategoriaCompleta:
                return EvaluarCategoriaCompleta(manoCardIds);

            case VictoryRuleType.CartaInfiltrada:
                return EvaluarCartaInfiltrada(manoCardIds, cartaInfiltradaId);

            case VictoryRuleType.CartaInfiltrada2:
                return EvaluarCartaInfiltrada2(manoCardIds, cartaInfiltradaId);

            default:
                return false;
        }
    }

    private static bool EvaluarCuatroIguales(List<int> mano)
    {
        int primero = mano[0];
        return mano.All(id => id == primero);
    }

    private static bool EvaluarCategoriaCompleta(List<int> mano)
    {
        List<CardData> cartas = mano
            .Select(id => CardDatabase.Instance.ObtenerPorId(id))
            .ToList();

        if (cartas.Any(carta => carta == null))
        {
            return false;
        }

        CardCategory categoria = cartas[0].category;

        bool todasLaMismaCategoria = cartas.All(carta => carta.category == categoria);

        if (!todasLaMismaCategoria)
        {
            return false;
        }

        // Tienen que ser las 4 cartas UNICAS de la categoria (no 4 copias
        // repetidas de la misma) - si no, ya seria "4 iguales", no esto.
        int cardIdsUnicos = cartas.Select(carta => carta.cardId).Distinct().Count();

        return cardIdsUnicos == 4;
    }

    /// <summary>
    /// Trio de 3 cartas DISTINTAS de una misma categoria (cualquiera) +
    /// la carta especifica que eligio el Boss.
    /// </summary>
    private static bool EvaluarCartaInfiltrada(List<int> mano, int cartaInfiltradaId)
    {
        if (!SepararCartaInfiltrada(mano, cartaInfiltradaId, out List<int> resto))
        {
            return false;
        }

        List<CardData> cartas = resto
            .Select(id => CardDatabase.Instance.ObtenerPorId(id))
            .ToList();

        if (cartas.Any(carta => carta == null))
        {
            return false;
        }

        CardCategory categoria = cartas[0].category;
        bool todasLaMismaCategoria = cartas.All(carta => carta.category == categoria);

        if (!todasLaMismaCategoria)
        {
            return false;
        }

        // El trio debe ser de 3 cartas UNICAS de esa categoria, no copias
        // repetidas - si no, seria CartaInfiltrada2, no esta.
        int cardIdsUnicos = cartas.Select(carta => carta.cardId).Distinct().Count();

        return cardIdsUnicos == 3;
    }

    /// <summary>
    /// 3 copias IDENTICAS de la misma carta + la carta especifica que
    /// eligio el Boss.
    /// </summary>
    private static bool EvaluarCartaInfiltrada2(List<int> mano, int cartaInfiltradaId)
    {
        if (!SepararCartaInfiltrada(mano, cartaInfiltradaId, out List<int> resto))
        {
            return false;
        }

        int primero = resto[0];
        return resto.All(id => id == primero);
    }

    /// <summary>
    /// Valida que la carta del Boss este presente en la mano (una sola vez)
    /// y devuelve las otras 3 cartas restantes para evaluar el trio.
    /// </summary>
    private static bool SepararCartaInfiltrada(List<int> mano, int cartaInfiltradaId, out List<int> resto)
    {
        resto = null;

        if (cartaInfiltradaId < 0 || !mano.Contains(cartaInfiltradaId))
        {
            return false;
        }

        resto = new List<int>(mano);
        resto.Remove(cartaInfiltradaId); // solo quita UNA ocurrencia

        return resto.Count == 3;
    }
}