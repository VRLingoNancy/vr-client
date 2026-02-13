using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class SceneHandler : MonoBehaviour
{
    public CanvasGroup fadeCanvas;
    public float fadeDuration = 1f;

    public void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }

    public void LoadSceneWithFade(string sceneName)
    {
        StartCoroutine(FadeAndLoad(sceneName));
    }

    private IEnumerator FadeAndLoad(string sceneName)
    {
        float t = 0;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            fadeCanvas.alpha = t / fadeDuration;
            yield return null;
        }
        SceneManager.LoadScene(sceneName);
    }

    public void Login()
    {
        const string login = "test@vrlingo.local";
        const string pass = "test1234";
    }
}