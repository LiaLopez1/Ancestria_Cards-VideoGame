using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel de notificación (tipo "toast") que muestra un mensaje como
/// "Carlos necesita cartas de Protectores" (pedido) o "Carlos tiene cartas
/// de Protectores" (mostrado), según la trampa usada. Se oculta solo
/// después de unos segundos.
///
/// Vive en la escena de juego (no persiste entre escenas).
/// </summary>
public class TrapNotificationUI : MonoBehaviour
{
    public static TrapNotificationUI Instance { get; private set; }

    [Header("Panel de notificación")]
    [SerializeField] private GameObject panelNotificacion;
    [SerializeField] private TMP_Text mensajeText;
    [SerializeField] private Image iconoImage;

    [Header("Duración")]
    [SerializeField] private float duracionVisible = 3f;

    private Coroutine ocultarCoroutine;

    private void Awake()
    {
        Instance = this;

        if (panelNotificacion != null)
        {
            panelNotificacion.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Llamado por TrapManager cuando alguien solicita ("necesita") o
    /// declara ("tiene") una categoría. <paramref name="tieneCategoria"/>
    /// decide el verbo del mensaje.
    /// </summary>
    public void MostrarNotificacion(string nombreJugador, string nombreCategoria, Sprite icono, bool tieneCategoria)
    {
        if (mensajeText != null)
        {
            string verbo = tieneCategoria ? "tiene" : "necesita";
            mensajeText.text = $"{nombreJugador} {verbo} cartas de {nombreCategoria}";
        }

        if (iconoImage != null)
        {
            iconoImage.sprite = icono;
        }

        if (panelNotificacion != null)
        {
            panelNotificacion.SetActive(true);
        }

        if (ocultarCoroutine != null)
        {
            StopCoroutine(ocultarCoroutine);
        }

        ocultarCoroutine = StartCoroutine(OcultarDespuesDeUnTiempo());
    }

    private IEnumerator OcultarDespuesDeUnTiempo()
    {
        yield return new WaitForSeconds(duracionVisible);

        if (panelNotificacion != null)
        {
            panelNotificacion.SetActive(false);
        }

        ocultarCoroutine = null;
    }
}