using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel de notificación (tipo "toast") que muestra un mensaje como
/// "Carlos necesita cartas de Protectores" (pedido) o "Carlos tiene cartas
/// de Protectores" (mostrado), según la trampa usada. Se oculta sola
/// después de unos segundos.
///
/// Si varias notificaciones llegan casi al mismo tiempo (dos jugadores
/// usando la trampa juntos), se ENCOLAN en vez de pisarse entre si - cada
/// una se muestra completa antes de pasar a la siguiente.
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

    [Header("Audio")]
    //[SerializeField] private SoundData trapNotificationSound;


    /// <summary>Una notificación esperando su turno en la cola.</summary>
    private struct NotificacionPendiente
    {
        public string nombreJugador;
        public string nombreCategoria;
        public Sprite icono;
        public bool tieneCategoria;
    }

    private readonly Queue<NotificacionPendiente> cola = new Queue<NotificacionPendiente>();
    private Coroutine corrutinaActual;

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
    /// decide el verbo del mensaje. Si ya hay una notificación visible (por
    /// ejemplo, dos jugadores usando la trampa casi al mismo tiempo), esta
    /// se encola y se muestra completa (su duracionVisible entera) apenas
    /// le toque el turno - nunca se pisan ni se pierden entre si.
    /// </summary>
    public void MostrarNotificacion(string nombreJugador, string nombreCategoria, Sprite icono, bool tieneCategoria)
    {
        cola.Enqueue(new NotificacionPendiente
        {
            nombreJugador = nombreJugador,
            nombreCategoria = nombreCategoria,
            icono = icono,
            tieneCategoria = tieneCategoria
        });

        if (corrutinaActual == null)
        {
            corrutinaActual = StartCoroutine(ProcesarCola());
        }
    }

    private IEnumerator ProcesarCola()
    {
        while (cola.Count > 0)
        {
            MostrarUnaNotificacion(cola.Dequeue());
            yield return new WaitForSeconds(duracionVisible);
        }

        if (panelNotificacion != null)
        {
            panelNotificacion.SetActive(false);
        }

        corrutinaActual = null;
    }

    private void MostrarUnaNotificacion(NotificacionPendiente notificacion)
    {
        if (mensajeText != null)
        {
            string verbo = notificacion.tieneCategoria ? "tiene" : "necesita";
            mensajeText.text = $"{notificacion.nombreJugador} {verbo} cartas de {notificacion.nombreCategoria}";
        }

        if (iconoImage != null)
        {
            iconoImage.sprite = notificacion.icono;
        }

        if (panelNotificacion != null)
        {
            panelNotificacion.SetActive(true);
        }

        //trapNotificationSound.Play();
    }
}