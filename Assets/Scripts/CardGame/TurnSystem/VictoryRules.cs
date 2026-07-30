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
        VictoryRuleType.CartaInfiltrada,
        VictoryRuleType.CartaInfiltrada2,
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

    /// <summary>Version larga, para mostrar debajo del titulo corto (ObtenerNombre) y explicar como se gana.</summary>
    public static string ObtenerDescripcion(VictoryRuleType regla)
    {
        switch (regla)
        {
            case VictoryRuleType.CuatroIguales:
                return "Consigue 4 copias exactas de la misma carta.";
            case VictoryRuleType.CategoriaCompleta:
                return "Consigue las 4 cartas distintas de una misma categoría.";
            case VictoryRuleType.CartaInfiltrada:
                return "Consigue 3 cartas distintas de una misma categoría, más cualquier carta de la categoría que reveló el Boss.";
            case VictoryRuleType.CartaInfiltrada2:
                return "Consigue 3 copias exactas de la misma carta, más cualquier carta de la categoría que reveló el Boss.";
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Evalua si una mano (lista de cardId) cumple la regla dada.
    /// Solo tiene sentido llamarlo del lado del servidor, ya que necesita
    /// la mano REAL del jugador (privada).
    /// </summary>
    /// <param name="categoriaInfiltrada">
    /// Solo se usa para CartaInfiltrada / CartaInfiltrada2 - la categoría
    /// que el Boss "muestra" al inicio de la ronda. Cualquier carta de esa
    /// categoría en la mano cuenta (no una carta exacta). Pasa null para
    /// las demas reglas, no hace falta.
    /// </param>
    public static bool SeCumple(VictoryRuleType regla, List<int> manoCardIds, CardCategory? categoriaInfiltrada = null)
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
                return EvaluarCartaInfiltrada(manoCardIds, categoriaInfiltrada);

            case VictoryRuleType.CartaInfiltrada2:
                return EvaluarCartaInfiltrada2(manoCardIds, categoriaInfiltrada);

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
    /// CUALQUIER carta de la categoria que "muestra" el Boss.
    ///
    /// Prueba CADA carta de la mano que pertenezca a la categoria infiltrada
    /// como posible comodin (no solo la primera que encuentre) - necesario
    /// para manos con copias repetidas de la misma carta, donde separar una
    /// u otra copia puede cambiar si el trio restante es valido o no.
    /// </summary>
    private static bool EvaluarCartaInfiltrada(List<int> mano, CardCategory? categoriaInfiltrada)
    {
        if (categoriaInfiltrada == null)
        {
            return false;
        }

        List<CardData> cartas = mano.Select(id => CardDatabase.Instance.ObtenerPorId(id)).ToList();

        if (cartas.Any(carta => carta == null))
        {
            return false;
        }

        for (int i = 0; i < mano.Count; i++)
        {
            if (cartas[i].category != categoriaInfiltrada.Value)
            {
                continue;
            }

            List<int> resto = new List<int>(mano);
            resto.RemoveAt(i);

            if (EsTrioDeCategoriaValido(resto))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 3 copias IDENTICAS de la misma carta + CUALQUIER carta de la
    /// categoria que "muestra" el Boss. Misma idea de probar todas las
    /// combinaciones posibles que EvaluarCartaInfiltrada.
    /// </summary>
    private static bool EvaluarCartaInfiltrada2(List<int> mano, CardCategory? categoriaInfiltrada)
    {
        if (categoriaInfiltrada == null)
        {
            return false;
        }

        List<CardData> cartas = mano.Select(id => CardDatabase.Instance.ObtenerPorId(id)).ToList();

        if (cartas.Any(carta => carta == null))
        {
            return false;
        }

        for (int i = 0; i < mano.Count; i++)
        {
            if (cartas[i].category != categoriaInfiltrada.Value)
            {
                continue;
            }

            List<int> resto = new List<int>(mano);
            resto.RemoveAt(i);

            int primero = resto[0];

            if (resto.All(id => id == primero))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>¿Estas 3 cartas son todas de la misma categoria y las 3 son DISTINTAS entre si?</summary>
    private static bool EsTrioDeCategoriaValido(List<int> tresCartas)
    {
        List<CardData> cartas = tresCartas.Select(id => CardDatabase.Instance.ObtenerPorId(id)).ToList();

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

        int cardIdsUnicos = cartas.Select(carta => carta.cardId).Distinct().Count();

        return cardIdsUnicos == 3;
    }
}