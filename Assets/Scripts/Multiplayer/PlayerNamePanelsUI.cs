using TMPro;
using UnityEngine;

/// <summary>
/// Controla los 3 paneles UI (dentro de un Canvas) que muestran el nick de
/// cada jugador conectado, en posiciones FIJAS de pantalla segun su slot
/// (0=host, 1=invitado1, 2=invitado2) - igual que los cubos, pero como UI en
/// vez de objetos 3D.
///
/// Vive en la escena de juego (no persiste entre escenas). PlayerCube llama a
/// ActualizarNombre() por cada jugador una vez conoce su slot y su nick.
/// </summary>
public class PlayerNamePanelsUI : MonoBehaviour
{
    public static PlayerNamePanelsUI Instance { get; private set; }

    [Header("Panel: host (slot 0)")]
    [SerializeField] private GameObject panelHost;
    [SerializeField] private TMP_Text nombreHostText;

    [Header("Panel: invitado 1 (slot 1)")]
    [SerializeField] private GameObject panelInvitado1;
    [SerializeField] private TMP_Text nombreInvitado1Text;

    [Header("Panel: invitado 2 (slot 2)")]
    [SerializeField] private GameObject panelInvitado2;
    [SerializeField] private TMP_Text nombreInvitado2Text;

    private void Awake()
    {
        Instance = this;

        // Arrancan ocultos: solo se muestran cuando ese slot realmente tiene
        // un jugador conectado con su nick ya conocido.
        if (panelHost != null) panelHost.SetActive(false);
        if (panelInvitado1 != null) panelInvitado1.SetActive(false);
        if (panelInvitado2 != null) panelInvitado2.SetActive(false);

        // El cubo del host puede spawnear en la escena de menu, ANTES de que
        // esta escena (y este panel) siquiera existan - por eso, apenas
        // estamos listos, le pedimos a todos los cubos que ya existan que
        // reintenten avisar su nombre.
        foreach (var cubo in FindObjectsOfType<PlayerCube>())
        {
            cubo.ReintentarAvisoDePanel();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void ActualizarNombre(int slot, string nombre)
    {
        if (slot == 0)
        {
            if (nombreHostText != null) nombreHostText.text = nombre;
            if (panelHost != null) panelHost.SetActive(true);
        }
        else if (slot == 1)
        {
            if (nombreInvitado1Text != null) nombreInvitado1Text.text = nombre;
            if (panelInvitado1 != null) panelInvitado1.SetActive(true);
        }
        else
        {
            if (nombreInvitado2Text != null) nombreInvitado2Text.text = nombre;
            if (panelInvitado2 != null) panelInvitado2.SetActive(true);
        }
    }

    /// <summary>
    /// Devuelve el nick ya conocido para ese slot (el mismo que se muestra
    /// en su panel), o un texto generico si todavia no se conoce. Lo usa
    /// TrapManager para armar el mensaje de notificacion de trampas.
    /// </summary>
    public string ObtenerNombrePorSlot(int slot)
    {
        TMP_Text texto = slot == 0 ? nombreHostText : slot == 1 ? nombreInvitado1Text : nombreInvitado2Text;

        if (texto != null && !string.IsNullOrEmpty(texto.text))
        {
            return texto.text;
        }

        return $"Jugador {slot}";
    }
}