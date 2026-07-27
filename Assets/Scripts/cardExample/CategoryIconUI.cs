using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Componente del prefab del icono que aparece junto al panel de nombre de
/// quien solicitó una categoría. Solo muestra el sprite que le pasen.
/// </summary>
public class CategoryIconUI : MonoBehaviour
{
    [SerializeField] private Image iconoImage;

    public void Configurar(Sprite icono)
    {
        if (iconoImage != null)
        {
            iconoImage.sprite = icono;
        }
    }
}