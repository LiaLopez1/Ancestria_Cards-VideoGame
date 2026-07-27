using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Todas las reglas de victoria posibles. Para agregar una nueva:
/// 1) Sumar un valor aqui.
/// 2) Agregarlo al array ReglasDisponibles.
/// 3) Agregar su caso en SeCumple() (y opcionalmente en ObtenerNombre()).
/// Nada mas necesita cambiar - ni TurnManager ni DeckManager conocen los
/// detalles de cada regla, solo llaman a VictoryRules.SeCumple(...).
/// </summary>
public enum VictoryRuleType
{
    CuatroIguales,
    CategoriaCompleta,
}

public static class VictoryRules
{
    /// <summary>
    /// Las reglas que de verdad estan activas para sortear en una partida.
    /// Es un array a proposito: agregar una regla nueva es agregarla aqui,
    /// nada mas.
    /// </summary>
    public static readonly VictoryRuleType[] ReglasDisponibles =
    {
        VictoryRuleType.CuatroIguales,
        VictoryRuleType.CategoriaCompleta,
    };

    public static string ObtenerNombre(VictoryRuleType regla)
    {
        switch (regla)
        {
            case VictoryRuleType.CuatroIguales:
                return "4 iguales";
            case VictoryRuleType.CategoriaCompleta:
                return "Categoría completa";
            default:
                return regla.ToString();
        }
    }

    /// <summary>
    /// Evalua si una mano (lista de cardId) cumple la regla dada.
    /// Solo tiene sentido llamarlo del lado del servidor, ya que necesita
    /// la mano REAL del jugador (privada).
    /// </summary>
    public static bool SeCumple(VictoryRuleType regla, List<int> manoCardIds)
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
}