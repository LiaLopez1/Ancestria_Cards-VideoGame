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

    [Header ("Información")]
    public string cardName;

    [Header ("Caracteristicas")]
    public CardCategory category;
    public Sprite icono;
    public Sprite character;

    //public CardColor color;

    public void Print()
    {
        Debug.Log("Nombre: "+ cardName +  "Categoria: "  + category + "Color: ");
    }
}
