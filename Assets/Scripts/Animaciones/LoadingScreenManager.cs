using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadingScreenManager : MonoBehaviour
{
    public static LoadingScreenManager Instance;

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Camera loadingCamera;
    [SerializeField] private float fadeDuration = 0.4f;
    [SerializeField] private float minLoadingDuration = 2.5f;
    [SerializeField] private float syncTimeout = 15f;

    private float startTime;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        canvasGroup.alpha = 0;
        canvasGroup.blocksRaycasts = false;
        loadingCamera.enabled = false;
    }

    public IEnumerator ShowLoading()
    {
        yield return Fade(0f, 1f);
        loadingCamera.enabled = true;
        startTime = Time.time;
    }

    // Para el HOST: llama LoadScene y espera el evento de Netcode
    public void LoadNetworkScene(string sceneName, System.Func<IEnumerator> extraWaitRoutine = null)
    {
        StartCoroutine(LoadRoutine(sceneName, extraWaitRoutine));
    }

    private IEnumerator LoadRoutine(string sceneName, System.Func<IEnumerator> extraWaitRoutine)
    {
        if (extraWaitRoutine != null)
            yield return StartCoroutine(extraWaitRoutine());

        bool sceneLoaded = false;
        void OnLoadCompleted(string s, LoadSceneMode m, System.Collections.Generic.List<ulong> ok, System.Collections.Generic.List<ulong> timeout)
        {
            sceneLoaded = true;
        }

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoadCompleted;
        NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);

        yield return new WaitUntil(() => sceneLoaded);
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadCompleted;

        yield return EsperarMinimoYFadeOut();
    }

    // Para el CLIENTE: revisa la escena activa en vez de depender del evento
    public void WaitForSceneSync(string expectedSceneName)
    {
        StartCoroutine(WaitForSyncRoutine(expectedSceneName));
    }

    private IEnumerator WaitForSyncRoutine(string expectedSceneName)
    {
        float t = 0f;

        while (SceneManager.GetActiveScene().name != expectedSceneName && t < syncTimeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (SceneManager.GetActiveScene().name != expectedSceneName)
        {
            Debug.LogWarning("[LoadingScreen] Timeout esperando sincronizacion de escena.");
        }

        yield return EsperarMinimoYFadeOut();
    }

    private IEnumerator EsperarMinimoYFadeOut()
    {
        float elapsed = Time.time - startTime;
        float remaining = minLoadingDuration - elapsed;
        if (remaining > 0f)
            yield return new WaitForSeconds(remaining);

        yield return Fade(1f, 0f);
        loadingCamera.enabled = false;
    }

    public void HideLoadingOnError()
    {
        StopAllCoroutines();
        StartCoroutine(HideImmediateRoutine());
    }

    private IEnumerator HideImmediateRoutine()
    {
        yield return Fade(1f, 0f);
        loadingCamera.enabled = false;
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