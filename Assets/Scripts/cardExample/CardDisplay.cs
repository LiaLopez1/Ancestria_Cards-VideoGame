using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CardDisplay : MonoBehaviour
{
    public CardData card;

    public TMP_Text textNombre;
    public TMP_Text textCategoria;
    public Image ImageColor;


    void Start()
    {
        card.Print();
        textNombre.text = card.cardName;
        textCategoria.text = card.category.ToString(); // esto despues se cambia a unn ícono   ImageColor.sprite = card.artwork; 
      


        cambiarColor();
    }

    void cambiarColor()
    {
        switch(card.color)
        {
            case CardColor.Rojo:
                ImageColor.color = Color.red;
                break;

            case CardColor.verde:
                    ImageColor.color = Color.green;
                    break;

            case CardColor.Azul:
                ImageColor.color = Color.blue;
                break;

            case CardColor.Morado:
                ImageColor.color = Color.purple;
                break;

        }  
    }



}
