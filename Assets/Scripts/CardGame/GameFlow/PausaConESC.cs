using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Activa/desactiva (toggle) el panel de pausa al apretar Esc - usa la API
/// del Input System nuevo (no la clase Input vieja, que tira
/// InvalidOperationException si el proyecto tiene el Input System como
/// unico sistema activo en Player Settings).
/// </summary>
public class PausaConEsc : MonoBehaviour
{
    [SerializeField] private GameObject panelPausa;

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (panelPausa != null)
            {
                panelPausa.SetActive(!panelPausa.activeSelf);
            }
        }
    }
}