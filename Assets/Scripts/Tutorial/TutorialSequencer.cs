using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Motor de pasos del tutorial. Cada paso muestra VIDEO, IMAGEN estatica, o
/// "Ninguno" (sigue con lo que ya estaba en pantalla, sin cortes - util
/// cuando varios paneles de texto pasan sobre el mismo video/imagen). Lo
/// que cambia paso a paso es:
///   - El texto del panel.
///   - La POSICION del panel en pantalla (para no tapar lo importante de
///     cada momento).
///   - El tipo de media (Video/Imagen/Ninguno) y, si corresponde, el clip
///     o sprite nuevo.
///
/// Cada paso avanza al siguiente de una de 3 formas, independientemente
/// del tipo de media:
///   - Duracion: se muestra unos segundos y avanza solo (sin boton).
///   - Click en el panel: aparece el boton "Siguiente", ya interactuable
///     desde que se muestra el paso.
///   - Fin de video: aparece el boton "Siguiente", pero arranca NO
///     interactuable - el video se queda en su ultimo frame al terminar
///     (Wrap Mode "Hold" en el VideoPlayer) y recien ahi se habilita el
///     boton para poder avanzar. El video NUNCA avanza el paso solo. SOLO
///     tiene sentido combinado con tipo de media "Video" (si se usa con
///     Imagen/Ninguno, el paso se traba - hay un warning en consola).
///
/// Al ser todo por video/imagen en vez de jugable, este script no necesita
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

    private enum TipoDeMedia
    {
        Video,
        Imagen,
        [InspectorName("Ninguno (sigue lo que ya estaba)")]
        Ninguno,
    }

    [Serializable]
    private class MarcadorDePaso
    {
        [Tooltip("El objeto YA UBICADO en la escena (por ejemplo, la flechita que rebota) - NO un prefab para instanciar. Mismo patron que turnHighlighter en TurnManager: este script solo lo activa/desactiva.")]
        public GameObject objetoEnEscena;

        [Tooltip("Si esta tildado, ademas de activarlo se lo reposiciona a 'Posicion'. Si no, se deja donde ya este puesto en la escena.")]
        public bool reposicionar;

        [Tooltip("Solo se usa si 'Reposicionar' esta tildado.")]
        public Vector2 posicion;

        [Tooltip("Si esta tildado, ademas de activarlo se lo rota a 'Rotacion'. Si no, se deja con la rotacion que ya tenga en la escena.")]
        public bool rotar;

        [Tooltip("Solo se usa si 'Rotar' esta tildado - grados en el eje Z (el que usa la UI).")]
        public float rotacion;
    }

    [Serializable]
    private class PasoTutorial
    {
        [TextArea(2, 5)]
        public string texto;

        [Tooltip("Posicion del panel en pantalla (anchoredPosition) para este paso - se mueve para no tapar lo importante del video/imagen en cada momento.")]
        public Vector2 posicionPanel;

        [Tooltip("Video, Imagen estatica, o Ninguno (sigue mostrando lo que ya estaba en el paso anterior, sin cortes - util cuando varios paneles pasan sobre el mismo video/imagen).")]
        public TipoDeMedia tipoDeMedia = TipoDeMedia.Video;

        [Tooltip("Solo se usa si Tipo De Media es Video.")]
        public VideoClip clip;

        [Tooltip("Solo se usa si Tipo De Media es Imagen.")]
        public Sprite imagen;

        public TipoDeEspera tipoDeEspera;

        [Tooltip("Solo se usa si el tipo de espera es 'Duracion'.")]
        public float duracionSegundos = 3f;

        [Tooltip("Objetos YA UBICADOS en la escena para ESTE paso en particular (por ejemplo, la flechita que rebota apuntando a algo) - agregá 0, 1, 2, o los que necesites. Se activan apenas se muestra el paso, y se desactivan automaticamente al pasar al siguiente.")]
        public List<MarcadorDePaso> marcadores = new List<MarcadorDePaso>();

        [Tooltip("Se ejecuta apenas se MUESTRA este paso (antes de que arranque la espera).")]
        public UnityEvent alMostrarse;
    }

    [Header("Panel reutilizable")]
    [SerializeField] private RectTransform panelRect;
    [SerializeField] private TMP_Text textoPanel;
    [Tooltip("Boton 'Siguiente'. En los pasos 'ClickEnPanel' aparece ya interactuable. En los pasos 'FinDeVideo' aparece pero NO interactuable hasta que el video termine - recien ahi se habilita. En los pasos 'Duracion' queda oculto (avanza solo).")]
    [SerializeField] private Button botonSiguiente;

    [Header("Video de fondo")]
    [SerializeField] private VideoPlayer videoPlayer;
    [Tooltip("El objeto que muestra el video en pantalla (por ejemplo un RawImage con RenderTexture) - se activa solo en los pasos de tipo Video.")]
    [SerializeField] private GameObject contenedorVideo;
    [Tooltip("El primer clip que arranca apenas empieza el tutorial, antes de mostrar el paso 1 (solo si el paso 1 es de tipo Video).")]
    [SerializeField] private VideoClip clipInicial;

    [Header("Imagen estatica de fondo")]
    [Tooltip("El objeto que muestra la imagen en pantalla - se activa solo en los pasos de tipo Imagen.")]
    [SerializeField] private GameObject contenedorImagen;
    [SerializeField] private Image imagenMostrada;

    [Header("Pasos, en orden")]
    [SerializeField] private List<PasoTutorial> pasos = new List<PasoTutorial>();

    /// <summary>Se dispara cuando se termino el ultimo paso configurado aca - a partir de ahi sigue la logica de "ganar la partida trucada".</summary>
    public event Action OnSecuenciaTerminada;

    private int indicePasoActual = -1;
    private Coroutine corrutinaActual;
    private readonly List<GameObject> marcadoresActivos = new List<GameObject>();

    private void Awake()
    {
        Instance = this;

        if (panelRect != null)
        {
            panelRect.gameObject.SetActive(false);
        }

        if (contenedorImagen != null)
        {
            contenedorImagen.SetActive(false);
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
            if (contenedorVideo != null)
            {
                contenedorVideo.SetActive(true);
            }

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

            LimpiarMarcadores();

            Debug.Log("[TutorialSequencer] Secuencia terminada - disparando OnSecuenciaTerminada.");
            OnSecuenciaTerminada?.Invoke();
            return;
        }

        MostrarPaso(pasos[indicePasoActual]);
    }

    private void MostrarPaso(PasoTutorial paso)
    {
        LimpiarMarcadores();

        if (textoPanel != null)
        {
            textoPanel.text = paso.texto;
        }

        if (panelRect != null)
        {
            panelRect.anchoredPosition = paso.posicionPanel;
            panelRect.gameObject.SetActive(true);
        }

        // Segun el tipo de media de este paso: cambia a un video nuevo,
        // cambia a una imagen nueva, o no toca nada (sigue lo que ya
        // estaba en pantalla del paso anterior, sin cortes).
        switch (paso.tipoDeMedia)
        {
            case TipoDeMedia.Video:
                MostrarVideo(paso.clip);
                break;

            case TipoDeMedia.Imagen:
                MostrarImagen(paso.imagen);
                break;

            case TipoDeMedia.Ninguno:
                // No se toca ni el video ni la imagen - sigue lo del paso anterior.
                break;
        }

        if (paso.tipoDeEspera == TipoDeEspera.FinDeVideo && paso.tipoDeMedia != TipoDeMedia.Video)
        {
            Debug.LogWarning("[TutorialSequencer] Un paso tiene tipo de espera 'FinDeVideo' pero su tipo de media NO es 'Video' - el video de ese momento nunca va a terminar (porque no es un video), asi que este paso se va a trabar. Revisar la configuracion de ese paso en el Inspector.");
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

        MostrarMarcadores(paso.marcadores);
    }

    /// <summary>
    /// Activa los marcadores de este paso - son objetos YA UBICADOS en la
    /// escena (no prefabs para instanciar), mismo patron que turnHighlighter
    /// en TurnManager. Solo los prende (y opcionalmente los reposiciona).
    /// </summary>
    private void MostrarMarcadores(List<MarcadorDePaso> marcadores)
    {
        if (marcadores == null)
        {
            return;
        }

        foreach (MarcadorDePaso marcador in marcadores)
        {
            if (marcador.objetoEnEscena == null)
            {
                continue;
            }

            // Posicion y rotacion se fijan ANTES de activar - asi, cuando
            // TurnHighlighterBounce (u otro componente similar) recaptura
            // su base en OnEnable, ya encuentra al objeto en el lugar
            // correcto.
            if (marcador.reposicionar)
            {
                RectTransform rectMarcador = marcador.objetoEnEscena.GetComponent<RectTransform>();

                if (rectMarcador != null)
                {
                    rectMarcador.anchoredPosition = marcador.posicion;
                }
                else
                {
                    marcador.objetoEnEscena.transform.localPosition = marcador.posicion;
                }
            }

            if (marcador.rotar)
            {
                marcador.objetoEnEscena.transform.localRotation = Quaternion.Euler(0f, 0f, marcador.rotacion);
            }

            marcador.objetoEnEscena.SetActive(true);

            marcadoresActivos.Add(marcador.objetoEnEscena);
        }
    }

    /// <summary>Apaga (SetActive false) todos los marcadores que quedaron prendidos del paso anterior - se llama antes de mostrar cualquier paso nuevo, y al terminar la secuencia. No destruye nada, son objetos persistentes de la escena.</summary>
    private void LimpiarMarcadores()
    {
        foreach (GameObject objeto in marcadoresActivos)
        {
            if (objeto != null)
            {
                objeto.SetActive(false);
            }
        }

        marcadoresActivos.Clear();
    }

    private void MostrarVideo(VideoClip clip)
    {
        if (contenedorImagen != null)
        {
            contenedorImagen.SetActive(false);
        }

        if (contenedorVideo != null)
        {
            contenedorVideo.SetActive(true);
        }

        if (clip != null && videoPlayer != null)
        {
            videoPlayer.clip = clip;
            videoPlayer.Play();
        }
    }

    private void MostrarImagen(Sprite sprite)
    {
        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            videoPlayer.Stop();
        }

        if (contenedorVideo != null)
        {
            contenedorVideo.SetActive(false);
        }

        if (contenedorImagen == null)
        {
            Debug.LogWarning("[TutorialSequencer] Este paso es de tipo Imagen, pero 'Contenedor Imagen' no está asignado en el Inspector - no hay donde mostrarla.");
            return;
        }

        contenedorImagen.SetActive(true);

        if (imagenMostrada == null)
        {
            Debug.LogWarning("[TutorialSequencer] Este paso es de tipo Imagen, pero 'Imagen Mostrada' no está asignado en el Inspector.");
            return;
        }

        if (sprite == null)
        {
            Debug.LogWarning("[TutorialSequencer] Este paso es de tipo Imagen, pero no se asignó ningún sprite en el campo 'Imagen' de ese paso.");
        }

        imagenMostrada.sprite = sprite;
    }

    private IEnumerator EsperarDuracion(float segundos)
    {
        yield return new WaitForSeconds(segundos);
        corrutinaActual = null;
        AvanzarAlSiguientePaso();
    }
}