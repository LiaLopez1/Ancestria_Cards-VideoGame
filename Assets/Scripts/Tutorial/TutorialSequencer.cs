using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Motor de pasos del tutorial. El VIDEO de fondo esta SIEMPRE presente
/// (reemplaza la partida jugable) y nunca se apaga durante todo el
/// tutorial - lo que cambia paso a paso es:
///   - El texto del panel.
///   - La POSICION del panel en pantalla (para no tapar lo importante de
///     cada momento del video).
///   - Opcionalmente, el clip que se esta reproduciendo de fondo.
///
/// Un paso puede asignar un clip nuevo (el video cambia apenas se muestra
/// ese paso) o dejarlo vacio (sigue el mismo clip de antes sin cortes -
/// util cuando varios paneles pasan sobre el mismo tramo de video).
///
/// Cada paso avanza al siguiente de una de 3 formas, independientemente de
/// si cambio de clip o no:
///   - Duracion: se muestra unos segundos y avanza solo (sin boton).
///   - Click en el panel: aparece el boton "Siguiente", ya interactuable
///     desde que se muestra el paso.
///   - Fin de video: aparece el boton "Siguiente", pero arranca NO
///     interactuable - el video se queda en su ultimo frame al terminar
///     (Wrap Mode "Hold" en el VideoPlayer) y recien ahi se habilita el
///     boton para poder avanzar. El video NUNCA avanza el paso solo.
///
/// Al ser todo por video en vez de jugable, este script no necesita
/// engancharse a ningun sistema real del juego (mazo, turnos, categorias,
/// intercambio) - queda 100% autocontenido, sin Netcode ni logica
/// duplicada del juego real.
///
/// La secuencia NO arranca sola al cargar la escena - espera a que
/// TutorialIntroPromptUI llame a IniciarSecuencia() (solo si el jugador
/// contesta que si quiere ver el tutorial).
/// </summary>
public class TutorialSequencer : MonoBehaviour
{
    public static TutorialSequencer Instance { get; private set; }

    private enum TipoDeEspera
    {
        Duracion,
        ClickEnPanel,
        FinDeVideo,
    }

    [Serializable]
    private class PasoTutorial
    {
        [TextArea(2, 5)]
        public string texto;

        [Tooltip("Posicion del panel en pantalla (anchoredPosition) para este paso - se mueve para no tapar lo importante del video en cada momento.")]
        public Vector2 posicionPanel;

        [Tooltip("Si se asigna, el video de fondo cambia a este clip apenas se muestra este paso. Si se deja vacio, el video sigue con lo que ya estaba reproduciendo, sin cortes.")]
        public VideoClip clip;

        public TipoDeEspera tipoDeEspera;

        [Tooltip("Solo se usa si el tipo de espera es 'Duracion'.")]
        public float duracionSegundos = 3f;

        [Tooltip("Se ejecuta apenas se MUESTRA este paso (antes de que arranque la espera).")]
        public UnityEvent alMostrarse;
    }

    [Header("Panel reutilizable")]
    [SerializeField] private RectTransform panelRect;
    [SerializeField] private TMP_Text textoPanel;
    [Tooltip("Boton 'Siguiente'. En los pasos 'ClickEnPanel' aparece ya interactuable. En los pasos 'FinDeVideo' aparece pero NO interactuable hasta que el video termine - recien ahi se habilita. En los pasos 'Duracion' queda oculto (avanza solo).")]
    [SerializeField] private Button botonSiguiente;

    [Header("Video de fondo (siempre activo durante todo el tutorial)")]
    [SerializeField] private VideoPlayer videoPlayer;
    [Tooltip("El primer clip que arranca apenas empieza el tutorial, antes de mostrar el paso 1.")]
    [SerializeField] private VideoClip clipInicial;

    [Header("Pasos, en orden")]
    [SerializeField] private List<PasoTutorial> pasos = new List<PasoTutorial>();

    /// <summary>Se dispara cuando se termino el ultimo paso configurado aca - a partir de ahi sigue la logica de "ganar la partida trucada".</summary>
    public event Action OnSecuenciaTerminada;

    private int indicePasoActual = -1;
    private Coroutine corrutinaActual;

    private void Awake()
    {
        Instance = this;

        if (panelRect != null)
        {
            panelRect.gameObject.SetActive(false);
        }

        if (botonSiguiente != null)
        {
            botonSiguiente.onClick.AddListener(ManejarClickSiguiente);
        }

        if (videoPlayer != null)
        {
            videoPlayer.playOnAwake = false;
            videoPlayer.loopPointReached += ManejarVideoTerminado;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= ManejarVideoTerminado;
        }
    }

    private void Start()
    {
        // Ya NO arranca solo - espera a que TutorialIntroPromptUI confirme
        // que el jugador quiere ver el tutorial (ver IniciarSecuencia()).
        // Si contesta que no, nunca se llega a reproducir nada de esto.
    }

    /// <summary>
    /// Llamado por TutorialIntroPromptUI cuando el jugador confirma que
    /// quiere ver el tutorial ("Si"). Recien aca arranca el video de fondo
    /// y el primer paso.
    /// </summary>
    public void IniciarSecuencia()
    {
        if (videoPlayer != null && clipInicial != null)
        {
            videoPlayer.clip = clipInicial;
            videoPlayer.Play();
        }

        AvanzarAlSiguientePaso();
    }

    private void ManejarClickSiguiente()
    {
        PasoTutorial pasoActual = ObtenerPasoActual();

        if (pasoActual == null)
        {
            return;
        }

        // Sirve para ambos tipos - en FinDeVideo, el boton arranca no
        // interactuable y recien se habilita cuando el video termina (ver
        // ManejarVideoTerminado), asi que si Unity permitio el click es
        // porque ya corresponde avanzar.
        if (pasoActual.tipoDeEspera == TipoDeEspera.ClickEnPanel || pasoActual.tipoDeEspera == TipoDeEspera.FinDeVideo)
        {
            AvanzarAlSiguientePaso();
        }
    }

    private void ManejarVideoTerminado(VideoPlayer vp)
    {
        PasoTutorial pasoActual = ObtenerPasoActual();

        if (pasoActual == null || pasoActual.tipoDeEspera != TipoDeEspera.FinDeVideo)
        {
            // El video sigue su Wrap Mode normal (loop / mantener ultimo
            // frame, configurable en el Inspector del VideoPlayer) - no
            // hacemos nada mas si este paso no depende de eso.
            return;
        }

        // El video termino - se queda en el ultimo frame (Wrap Mode "Hold"
        // en el VideoPlayer) y recien ahora se habilita "Siguiente".
        if (botonSiguiente != null)
        {
            botonSiguiente.interactable = true;
        }
    }

    private PasoTutorial ObtenerPasoActual()
    {
        return indicePasoActual >= 0 && indicePasoActual < pasos.Count ? pasos[indicePasoActual] : null;
    }

    private void AvanzarAlSiguientePaso()
    {
        if (corrutinaActual != null)
        {
            StopCoroutine(corrutinaActual);
            corrutinaActual = null;
        }

        indicePasoActual++;

        if (indicePasoActual >= pasos.Count)
        {
            // Se acabaron los pasos instructivos - el video de fondo sigue
            // (o lo corta quien maneje la partida "trucada" despues, si
            // corresponde). Solo ocultamos el panel de texto.
            if (panelRect != null)
            {
                panelRect.gameObject.SetActive(false);
            }

            OnSecuenciaTerminada?.Invoke();
            return;
        }

        MostrarPaso(pasos[indicePasoActual]);
    }

    private void MostrarPaso(PasoTutorial paso)
    {
        if (textoPanel != null)
        {
            textoPanel.text = paso.texto;
        }

        if (panelRect != null)
        {
            panelRect.anchoredPosition = paso.posicionPanel;
            panelRect.gameObject.SetActive(true);
        }

        // Solo cambiamos de clip si este paso trajo uno nuevo - si no, el
        // video de fondo sigue igual que en el paso anterior, sin cortes.
        if (paso.clip != null && videoPlayer != null)
        {
            videoPlayer.clip = paso.clip;
            videoPlayer.Play();
        }

        paso.alMostrarse?.Invoke();

        switch (paso.tipoDeEspera)
        {
            case TipoDeEspera.Duracion:
                // Avanza solo - el boton no hace falta, se oculta.
                if (botonSiguiente != null)
                {
                    botonSiguiente.gameObject.SetActive(false);
                }

                corrutinaActual = StartCoroutine(EsperarDuracion(paso.duracionSegundos));
                break;

            case TipoDeEspera.ClickEnPanel:
                // No depende de ningun video - el boton ya arranca interactuable.
                if (botonSiguiente != null)
                {
                    botonSiguiente.gameObject.SetActive(true);
                    botonSiguiente.interactable = true;
                }
                break;

            case TipoDeEspera.FinDeVideo:
                // Se ve el boton, pero recien se habilita cuando el video
                // termine (ManejarVideoTerminado) - hasta entonces, no se
                // puede avanzar apretandolo.
                if (botonSiguiente != null)
                {
                    botonSiguiente.gameObject.SetActive(true);
                    botonSiguiente.interactable = false;
                }
                break;
        }
    }

    private IEnumerator EsperarDuracion(float segundos)
    {
        yield return new WaitForSeconds(segundos);
        corrutinaActual = null;
        AvanzarAlSiguientePaso();
    }
}