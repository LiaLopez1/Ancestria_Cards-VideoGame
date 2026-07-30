using System.Collections;
using System.Diagnostics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadingScreenManager : MonoBehaviour
{
    public static LoadingScreenManager Instance;

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float fadeDuration = 0.4f;
    [SerializeField] private float minLoadingDuration = 2.5f;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        canvasGroup.alpha = 0;
        canvasGroup.blocksRaycasts = false;
    }

    public void LoadNetworkScene(string sceneName, System.Func<IEnumerator> extraWaitRoutine = null)
    {
        StartCoroutine(LoadRoutine(sceneName, extraWaitRoutine));
    }

    private IEnumerator LoadRoutine(string sceneName, System.Func<IEnumerator> extraWaitRoutine)
    {
        // 1. Fade a negro ANTES de pedirle a NetworkManager que cargue la escena
        yield return Fade(0f, 1f);
        float startTime = Time.time;

        // 2. Esperar algo extra si hace falta (ej. respuesta de PlayFab)
        if (extraWaitRoutine != null)
            yield return StartCoroutine(extraWaitRoutine());
        //if (extraWaitRoutine != null)
           // yield return StartCoroutine(extraWaitRoutine());

        // 3. Suscribirse al evento ANTES de pedir la carga
        bool sceneLoaded = false;
        void OnLoadCompleted(string sceneNameLoaded, LoadSceneMode mode, System.Collections.Generic.List<ulong> clientsCompleted, System.Collections.Generic.List<ulong> clientsTimedOut)
        {
            sceneLoaded = true;
        }

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoadCompleted;

        // 4. Pedir la carga vía NetworkManager (como ya lo tienes)
        System.Diagnostics.Debug.WriteLine("Fade a negro completado, alpha=" + canvasGroup.alpha);
        NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        System.Diagnostics.Debug.WriteLine("Fade a negro completado, alpha=" + canvasGroup.alpha);

        // 5. Esperar a que la carga (y sincronización de red) termine
        yield return new WaitUntil(() => sceneLoaded);

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadCompleted;

        float elapsed = Time.time - startTime;
        float remaining = minLoadingDuration - elapsed;

        if (remaining > 0f)
            yield return new WaitForSeconds(remaining);

        // 6. Fade de vuelta
        yield return Fade(1f, 0f);
    }

    private IEnumerator Fade(float from, float to)
    {
        canvasGroup.blocksRaycasts = to > 0;
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, to, t / fadeDuration);
            yield return null;
        }
        canvasGroup.alpha = to;
    }
}