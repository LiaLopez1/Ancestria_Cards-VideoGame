using UnityEngine;

public enum CardCategory
{
    Protectores,
    Castigadores,
    Transformados,
    Apariciones,
}

/*public enum CardColor
{
    Rojo,
    Azul,
    verde,
    Morado,
}*/



[CreateAssetMenu(fileName = "New Card", menuName = "Cards/Card")]
public class CardData : ScriptableObject
{

    //public Sprite artwork; ejemplo en el video

    [Header ("Identidad (para multijugador)")]
    [Tooltip("Debe ser unico entre TODAS las cartas del juego y nunca cambiar una vez asignado. Se usa para sincronizar la carta por red en vez de mandar el ScriptableObject completo.")]
    public int cardId;

    [Header ("Información")]
    public string cardName;

    [Header ("Caracteristicas")]
    public CardCategory category;
    public Sprite icono;
    public Sprite character;
    public Sprite Fondo;

    //public CardColor color;

    public void Print()
    {
        Debug.Log("Nombre: "+ cardName +  "Categoria: "  + category + "Color: ");
    }
}