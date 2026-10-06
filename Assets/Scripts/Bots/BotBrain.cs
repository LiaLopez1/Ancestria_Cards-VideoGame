using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Personalidad" de un bot. Se configura desde el Inspector (en
/// BotController), así cada bot puede ser distinto: uno cauteloso y otro
/// arriesgado, sin tocar código.
/// </summary>
[System.Serializable]
public class BotPerfil
{
    public string nombre = "Bot";

    [Tooltip("0 = muy cauteloso (espera a que el boss revise cartas), 1 = no le importa que el boss mire al frente.")]
    [Range(0f, 1f)] public float audacia = 0.5f;

    [Tooltip("Fracción de la barra de sospecha (0-1) a partir de la cual el bot deja de hacer trampa.")]
    [Range(0f, 1f)] public float sospechaTope = 0.7f;

    [Tooltip("Segundos de espera (mín, máx) entre una trampa y la siguiente de ESTE bot.")]
    public Vector2 cooldownTrampa = new Vector2(8f, 18f);

    [Tooltip("Probabilidad de mostrar una categoría cuando alguien la pidió y él puede darla.")]
    [Range(0f, 1f)] public float probMostrar = 0.6f;

    [Tooltip("Probabilidad base de aceptar un intercambio que le proponen.")]
    [Range(0f, 1f)] public float probAceptarIntercambio = 0.6f;

    [Tooltip("Probabilidad de proponer un intercambio a alguien al azar cuando nadie ha mostrado lo que necesita.")]
    [Range(0f, 1f)] public float probIntercambioEspeculativo = 0.15f;

    [Tooltip("Cuánto tiempo (s) recuerda un pedido o una muestra pública antes de olvidarlos.")]
    public float vidaMemoria = 45f;
}

/// <summary>
/// Cerebro de UN bot. Clase de C# pura: no sabe nada de Netcode ni de
/// escenas, solo recibe datos (mano, regla, sospecha...) y responde con
/// decisiones. Por eso es fácil de probar y de ajustar.
///
/// Reglas de juego limpio: el bot usa SOLO su propia mano, la regla activa y
/// la información PÚBLICA (pedidos y muestras de categorías que todos
/// vieron). Nunca mira la mano de otro jugador.
///
/// Para elegir qué carta sobra reutiliza BossStrategy, que ya resuelve las 4
/// reglas de victoria.
///
/// Los métodos "Debe..." tiran dados (Random), así que BotController debe
/// llamarlos en momentos discretos (cada 1-3 segundos), no en cada frame.
/// </summary>
public class BotBrain
{
    private class Registro
    {
        public int slot;
        public CardCategory categoria;
        public float tiempo;
        public bool atendido;
    }

    private readonly BotPerfil perfil;
    private readonly int miSlot;

    private readonly List<Registro> pedidos = new List<Registro>();
    private readonly List<Registro> muestras = new List<Registro>();

    private float proximaTrampaPermitida;

    // A quién le prometí (mostré) una categoría, para aceptarle el intercambio.
    private int ultimoSlotAtendido = -1;
    private float tiempoUltimaMuestra = -999f;

    public BotBrain(BotPerfil perfil, int miSlot)
    {
        this.perfil = perfil;
        this.miSlot = miSlot;
    }

    public int MiSlot => miSlot;
    public string Nombre => perfil.nombre;

    /// <summary>Llamar al empezar cada ronda: olvida todo lo de la ronda anterior.</summary>
    public void Reiniciar()
    {
        pedidos.Clear();
        muestras.Clear();
        proximaTrampaPermitida = 0f;
        ultimoSlotAtendido = -1;
        tiempoUltimaMuestra = -999f;
    }

    // ------------------------------------------------------------------
    // Memoria pública
    // ------------------------------------------------------------------

    /// <summary>Alguien (humano u otro bot) pidió una categoría. Los propios pedidos se ignoran.</summary>
    public void RegistrarPedido(int slot, CardCategory categoria, float ahora)
    {
        if (slot == miSlot) return;
        pedidos.Add(new Registro { slot = slot, categoria = categoria, tiempo = ahora });
    }

    /// <summary>Alguien mostró que tiene una categoría. Las propias muestras se ignoran.</summary>
    public void RegistrarMuestra(int slot, CardCategory categoria, float ahora)
    {
        if (slot == miSlot) return;
        muestras.Add(new Registro { slot = slot, categoria = categoria, tiempo = ahora });
    }

    /// <summary>Llamar DESPUÉS de que este bot hizo una trampa (pedir, mostrar o proponer intercambio).</summary>
    public void RegistrarTrampaHecha(float ahora)
    {
        proximaTrampaPermitida = ahora + Random.Range(perfil.cooldownTrampa.x, perfil.cooldownTrampa.y);
    }

    /// <summary>Llamar DESPUÉS de que este bot mostró una categoría pedida por slotPedidor.</summary>
    public void RegistrarMuestraPropia(int slotPedidor, float ahora)
    {
        ultimoSlotAtendido = slotPedidor;
        tiempoUltimaMuestra = ahora;
    }

    private void Purgar(float ahora)
    {
        pedidos.RemoveAll(r => ahora - r.tiempo > perfil.vidaMemoria);
        muestras.RemoveAll(r => ahora - r.tiempo > perfil.vidaMemoria);
    }

    /// <summary>Slot de quien mostró esa categoría más recientemente, o -1 si nadie.</summary>
    private int QuienMostro(CardCategory categoria, float ahora)
    {
        for (int i = muestras.Count - 1; i >= 0; i--)
        {
            Registro m = muestras[i];

            if (m.categoria == categoria && ahora - m.tiempo <= perfil.vidaMemoria)
            {
                return m.slot;
            }
        }

        return -1;
    }

    // ------------------------------------------------------------------
    // Riesgo: ¿es buen momento para hacer trampa?
    // ------------------------------------------------------------------

    /// <param name="sospechaNorm">Nivel de sospecha dividido por el máximo (0-1).</param>
    /// <param name="bossMirandoAlFrente">true = el boss mira a los jugadores (la trampa suma 2 en vez de 1).</param>
    private bool PuedeHacerTrampa(float sospechaNorm, bool bossMirandoAlFrente, float ahora)
    {
        if (ahora < proximaTrampaPermitida) return false;
        if (sospechaNorm >= perfil.sospechaTope) return false;

        // Un bot cauteloso (audacia baja) casi siempre espera a que el boss
        // esté revisando sus cartas, que es cuando la trampa sale más barata.
        if (bossMirandoAlFrente && Random.value > perfil.audacia) return false;

        return true;
    }

    // ------------------------------------------------------------------
    // Jugar: descarte
    // ------------------------------------------------------------------

    /// <summary>Qué carta descartar (o dar en un intercambio). Devuelve -1 si la mano está vacía.</summary>
    public int ElegirDescarte(List<int> mano, VictoryRuleType regla, CardCategory? infiltrada)
    {
        int descarteOriginal = BossStrategy.ElegirCartaADescartar(mano, regla, infiltrada);

        CardData cartaOriginal = CardDatabase.Instance.ObtenerPorId(descarteOriginal);

        if (cartaOriginal == null) return descarteOriginal;

        if (!CategoriaFuePedidaRecientemente(cartaOriginal.category, Time.time))
        {
            return descarteOriginal;
        }

        foreach (int id in mano)
        {
            if (id == descarteOriginal) continue;

            CardData alternativa = CardDatabase.Instance.ObtenerPorId(id);

            if (alternativa != null && !CategoriaFuePedidaRecientemente(alternativa.category, Time.time))
            {
                Debug.Log($"[Bots][Decisión] {Nombre} evita descartar {cartaOriginal.category} porque otro jugador la pidió.");

                return id;
            }
        }

        return descarteOriginal;
    }

    // ------------------------------------------------------------------
    // ¿Qué categoría le falta para ganar? (solo cuando está a UNA carta)
    // ------------------------------------------------------------------

    /// <summary>
    /// Mano de 4 cartas -> categoría de la carta que le falta para ganar, o
    /// null si todavía está a más de una carta. Es pública y estática para
    /// poder probarla suelta.
    /// </summary>
    public static CardCategory? CategoriaNecesaria(List<int> mano, VictoryRuleType regla, CardCategory? infiltrada)
    {
        if (mano == null || mano.Count != 4) return null;

        switch (regla)
        {
            case VictoryRuleType.CuatroIguales:
                // 3 copias iguales: le falta la 4ª, de la misma categoría.
                return CategoriaDeCartaRepetida(mano, 3);

            case VictoryRuleType.CategoriaCompleta:
                // 3 cartas distintas de una categoría: le falta la 4ª.
                return CategoriaConDistintos(mano, 3);

            case VictoryRuleType.CartaInfiltrada:
            {
                if (infiltrada == null) return null;

                int idx = IndiceDeCategoria(mano, infiltrada.Value);

                if (idx >= 0)
                {
                    // Ya tiene el comodín: de las otras 3, necesita un trío
                    // de una categoría -> si tiene 2 distintas, le falta la 3ª.
                    List<int> resto = new List<int>(mano);
                    resto.RemoveAt(idx);
                    return CategoriaConDistintos(resto, 2);
                }

                // Sin comodín: si ya tiene un trío listo, solo le falta la categoría infiltrada.
                return CategoriaConDistintos(mano, 3).HasValue ? infiltrada : null;
            }

            case VictoryRuleType.CartaInfiltrada2:
            {
                if (infiltrada == null) return null;

                int idx = IndiceDeCategoria(mano, infiltrada.Value);

                if (idx >= 0)
                {
                    List<int> resto = new List<int>(mano);
                    resto.RemoveAt(idx);
                    return CategoriaDeCartaRepetida(resto, 2);
                }

                return CategoriaDeCartaRepetida(mano, 3).HasValue ? infiltrada : null;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Si tiene 5 cartas (recién robó), le quita la que descartaría, para
    /// evaluar qué le falta con la mano con la que se quedaría.
    /// </summary>
    private static List<int> ManoEfectiva(List<int> mano, VictoryRuleType regla, CardCategory? infiltrada)
    {
        List<int> copia = new List<int>(mano);

        if (copia.Count == 5)
        {
            int sobrante = BossStrategy.ElegirCartaADescartar(copia, regla, infiltrada);
            copia.Remove(sobrante);
        }

        return copia;
    }

    // ------------------------------------------------------------------
    // Trampa 1: PEDIR categoría
    // ------------------------------------------------------------------

    /// <summary>
    /// ¿Debe pedir una categoría ahora? Solo si está a una carta de ganar,
    /// es buen momento, y nadie ha mostrado ya esa categoría (si alguien la
    /// mostró, conviene más proponerle un intercambio que volver a pedir).
    /// </summary>
    public bool DebePedirCategoria(List<int> mano, VictoryRuleType regla, CardCategory? infiltrada,
        float sospechaNorm, bool bossMirandoAlFrente, float ahora, out CardCategory categoria)
    {
        categoria = default;
        Purgar(ahora);

        if (mano == null) return false;

        CardCategory? necesaria = CategoriaNecesaria(ManoEfectiva(mano, regla, infiltrada), regla, infiltrada);

        if (!necesaria.HasValue) return false;
        if (QuienMostro(necesaria.Value, ahora) >= 0) return false;
        if (!PuedeHacerTrampa(sospechaNorm, bossMirandoAlFrente, ahora)) return false;

        categoria = necesaria.Value;
        return true;
    }

    // ------------------------------------------------------------------
    // Trampa 2: MOSTRAR categoría
    // ------------------------------------------------------------------

    /// <summary>
    /// ¿Debe mostrar una categoría porque alguien la pidió? Solo ofrece una
    /// categoría si la carta que le SOBRA es de esa categoría (no regala lo
    /// que necesita). Cada pedido se evalúa UNA sola vez (una tirada de
    /// dados), así no insiste ni muestra todo el tiempo.
    /// </summary>
    public bool DebeMostrarCategoria(List<int> mano, VictoryRuleType regla, CardCategory? infiltrada,
        float sospechaNorm, bool bossMirandoAlFrente, float ahora, out CardCategory categoria, out int slotPedidor)
    {
        categoria = default;
        slotPedidor = -1;
        Purgar(ahora);

        if (mano == null || mano.Count == 0) return false;


        foreach (Registro pedido in pedidos)
        {
            if (pedido.atendido) continue;
            if (!TieneCategoria(mano, pedido.categoria)) continue;

            pedido.atendido = true; // una sola tirada por pedido

            if (Random.value > perfil.probMostrar) continue;

            categoria = pedido.categoria;
            slotPedidor = pedido.slot;
            return true;
        }

        return false;
    }

    // ------------------------------------------------------------------
    // Trampa 3: INTERCAMBIO (en su propio turno, con 5 cartas)
    // ------------------------------------------------------------------

    /// <summary>
    /// ¿Debe proponer un intercambio? Solo en su turno (5 cartas), si está a
    /// una carta de ganar. Prefiere a quien mostró la categoría que necesita;
    /// si nadie la mostró, a veces prueba suerte con alguien al azar.
    /// </summary>
    /// <param name="otrosSlots">Slots de los demás jugadores (SIN el boss y SIN él mismo).</param>
    public bool DebeProponerIntercambio(List<int> mano5, VictoryRuleType regla, CardCategory? infiltrada,
        IReadOnlyList<int> otrosSlots, float sospechaNorm, bool bossMirandoAlFrente, float ahora,
        out int slotObjetivo, out int cardIdOfrecido)
    {
        slotObjetivo = -1;
        cardIdOfrecido = -1;
        Purgar(ahora);

        if (mano5 == null || mano5.Count != 5) return false;

        CardCategory? necesaria = CategoriaNecesaria(ManoEfectiva(mano5, regla, infiltrada), regla, infiltrada);

        if (!necesaria.HasValue) return false;
        if (!PuedeHacerTrampa(sospechaNorm, bossMirandoAlFrente, ahora)) return false;

        int objetivo = QuienMostro(necesaria.Value, ahora);

        if (objetivo < 0)
        {
            if (otrosSlots == null || otrosSlots.Count == 0) return false;
            if (Random.value > perfil.probIntercambioEspeculativo) return false;

            objetivo = otrosSlots[Random.Range(0, otrosSlots.Count)];
        }

        slotObjetivo = objetivo;
        cardIdOfrecido = BossStrategy.ElegirCartaADescartar(mano5, regla, infiltrada);

        return cardIdOfrecido >= 0;
    }

    /// <summary>
    /// Otro jugador le propone un intercambio: ¿acepta? Más probable si el
    /// iniciador es a quien le mostró una categoría hace poco (cumple su
    /// palabra); mucho menos probable si la sospecha ya está alta, porque el
    /// intercambio sigue sumando sospecha mientras dura.
    /// </summary>
    public bool AceptaIntercambio(int slotIniciador, float sospechaNorm, float ahora)
    {
        float p = perfil.probAceptarIntercambio;

        if (sospechaNorm >= perfil.sospechaTope)
        {
            p *= 0.25f;
        }

        bool cumpleSuPalabra = slotIniciador == ultimoSlotAtendido
            && ahora - tiempoUltimaMuestra <= perfil.vidaMemoria;

        if (cumpleSuPalabra)
        {
            p = Mathf.Max(p, 0.9f);
        }

        return Random.value < p;
    }

    // ------------------------------------------------------------------
    // Utilidades de agrupación (privadas)
    // ------------------------------------------------------------------
    private static bool TieneCategoria(List<int> mano, CardCategory categoria)
    {
        foreach (int id in mano)
        {
            CardData carta = CardDatabase.Instance.ObtenerPorId(id);

            if (carta != null && carta.category == categoria)
            {
                return true;
            }
        }

        return false;
    }

    private bool CategoriaFuePedidaRecientemente(CardCategory categoria, float ahora)
    {
        Purgar(ahora);

        foreach (Registro pedido in pedidos)
        {
            if (pedido.categoria == categoria)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Categoría de un cardId que aparece EXACTAMENTE 'cantidadExacta' veces, o null.</summary>
    private static CardCategory? CategoriaDeCartaRepetida(List<int> cartas, int cantidadExacta)
    {
        Dictionary<int, int> conteo = new Dictionary<int, int>();

        foreach (int id in cartas)
        {
            conteo[id] = conteo.TryGetValue(id, out int actual) ? actual + 1 : 1;
        }

        foreach (KeyValuePair<int, int> par in conteo)
        {
            if (par.Value != cantidadExacta) continue;

            CardData carta = CardDatabase.Instance.ObtenerPorId(par.Key);

            if (carta != null)
            {
                return carta.category;
            }
        }

        return null;
    }

    /// <summary>Categoría que tiene EXACTAMENTE 'cantidad' cardIds DISTINTOS entre las cartas dadas, o null.</summary>
    private static CardCategory? CategoriaConDistintos(List<int> cartas, int cantidad)
    {
        Dictionary<CardCategory, HashSet<int>> porCategoria = new Dictionary<CardCategory, HashSet<int>>();

        foreach (int id in cartas)
        {
            CardData carta = CardDatabase.Instance.ObtenerPorId(id);

            if (carta == null) continue;

            if (!porCategoria.TryGetValue(carta.category, out HashSet<int> distintos))
            {
                distintos = new HashSet<int>();
                porCategoria[carta.category] = distintos;
            }

            distintos.Add(id);
        }

        foreach (KeyValuePair<CardCategory, HashSet<int>> par in porCategoria)
        {
            if (par.Value.Count == cantidad)
            {
                return par.Key;
            }
        }

        return null;
    }

    /// <summary>Posición de la primera carta de esa categoría dentro de la lista, o -1.</summary>
    private static int IndiceDeCategoria(List<int> cartas, CardCategory categoria)
    {
        for (int i = 0; i < cartas.Count; i++)
        {
            CardData carta = CardDatabase.Instance.ObtenerPorId(cartas[i]);

            if (carta != null && carta.category == categoria)
            {
                return i;
            }
        }

        return -1;
    }
}