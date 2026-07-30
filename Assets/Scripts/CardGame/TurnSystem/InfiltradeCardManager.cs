using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Elige la "categoría infiltrada" de la ronda - narrativamente es el Boss
/// quien la muestra al inicio de la partida (su icono), pero la elección
/// real vive aca, separada de BossManager.
///
/// Antes elegía una carta ESPECIFICA (cardId); ahora elige una CATEGORIA
/// completa, para que la regla sea mas facil: cualquier carta de esa
/// categoría en la mano cuenta, no una exacta.
///
/// Autoridad de servidor: SOLO el servidor elige (ElegirCategoriaInfiltrada).
/// Se sincroniza a todos los clientes vía NetworkVariable porque es
/// información PÚBLICA.
///
/// VictoryRules.CartaInfiltrada y CartaInfiltrada2 necesitan este valor -
/// quien llame a VictoryRules.SeCumple() para esas dos reglas debe pasar
/// CategoriaInfiltrada como tercer argumento.
/// </summary>
public class InfiltratedCardManager : NetworkBehaviour
{
    // -1 = todavia no se elige ninguna (o la regla de esta ronda no la usa).
    // Se guarda como int (no como CardCategory) porque NetworkVariable
    // necesita poder representar "ninguna" - los enums no tienen ese valor.
    private readonly NetworkVariable<int> categoriaInfiltradaIndex = new NetworkVariable<int>(-1);

    public bool HayCategoriaInfiltrada => categoriaInfiltradaIndex.Value >= 0;

    /// <summary>Solo valido si HayCategoriaInfiltrada es true.</summary>
    public CardCategory CategoriaInfiltrada => (CardCategory)categoriaInfiltradaIndex.Value;

    /// <summary>Para que la UI reaccione sin polling. Pasa el indice crudo (-1 = ninguna).</summary>
    public event Action<int> OnCategoriaInfiltradaElegida;

    public override void OnNetworkSpawn()
    {
        categoriaInfiltradaIndex.OnValueChanged += (anterior, nuevo) => OnCategoriaInfiltradaElegida?.Invoke(nuevo);
    }

    /// <summary>SOLO servidor - llamarlo al inicio de cada ronda que use una regla de carta infiltrada.</summary>
    public void ElegirCategoriaInfiltrada()
    {
        if (!IsServer)
        {
            return;
        }

        int totalCategorias = Enum.GetValues(typeof(CardCategory)).Length;
        categoriaInfiltradaIndex.Value = UnityEngine.Random.Range(0, totalCategorias);

        Debug.Log($"[Servidor] Categoría infiltrada elegida: {(CardCategory)categoriaInfiltradaIndex.Value}.");
    }

    /// <summary>SOLO servidor. Vuelve a -1 (ninguna) - para rondas cuya regla no usa esto.</summary>
    public void ReiniciarCategoriaInfiltrada()
    {
        if (!IsServer)
        {
            return;
        }

        categoriaInfiltradaIndex.Value = -1;
    }
}