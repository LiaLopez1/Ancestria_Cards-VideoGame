using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controla un item individual de la lista de salas disponibles.
/// El panel completo tiene 3 partes independientes:
/// - El nombre de la sala ("Juego de <nick>")
/// - El contador de jugadores ("2/3"), separado del nombre
/// - El boton para unirse, independiente de los textos
/// </summary>
public class SalaListItemUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nombreSalaText;
    [SerializeField] private TMP_Text jugadoresText;
    [SerializeField] private Button unirseButton;

    /// <summary>
    /// Configura este item con los datos de la sala encontrada.
    /// </summary>
    /// <param name="nombreSala">Ej: "Juego de Carlos"</param>
    /// <param name="jugadoresLabel">Ej: "2/3"</param>
    /// <param name="alUnirse">Que hacer cuando se presiona el boton Unirse</param>
    public void Configurar(string nombreSala, string jugadoresLabel, Action alUnirse)
    {
        nombreSalaText.text = nombreSala;
        jugadoresText.text = jugadoresLabel;

        unirseButton.onClick.RemoveAllListeners();
        unirseButton.onClick.AddListener(() => alUnirse?.Invoke());
    }
}