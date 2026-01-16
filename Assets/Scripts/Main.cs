using System;
using System.Collections;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Object = System.Object;

public class Main : MonoBehaviour
{
    private const string EXIT_TIP = "再按一次退出应用";
    private const float BACK_PRESS_THRESHOLD = 2f;
    private const int POPUP_GRAVITY = 17; // Gravity.CENTER

    public Text displayText;
    public InputField urlInputField;

    private float _lastBackPressedTime;
    private Coroutine _clearCoroutine;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            HandleBackPressed();
        }
    }
    private byte[] _closeBtnPngData;
#if UNITY_ANDROID
    private AndroidWebViewHelper _webViewHelper;
#endif

    private void Awake()
    {
#if UNITY_ANDROID
        _webViewHelper = new AndroidWebViewHelper();
#endif
        GetPNGData();
    }
    
    private void GetPNGData()
    {
        Sprite closeBtn = Resources.Load<Sprite>("Sprites/CloseBtn");
        if (closeBtn == null)
        {
            Debug.LogError("Failed to load Sprites/CloseBtn");
            return;
        }

        // 使用 RenderTexture 避免纹理可读限制
        Rect rect = closeBtn.textureRect;
        int width = (int)rect.width;
        int height = (int)rect.height;

        // 创建临时 RenderTexture
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        // 将 Sprite 渲染到 RenderTexture
        Graphics.Blit(closeBtn.texture, rt);

        // 从 RenderTexture 读取到 Texture2D
        Texture2D tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
        tex.ReadPixels(new Rect(rect.x, rect.y, width, height), 0, 0);
        tex.Apply();

        // 编码为 PNG
        _closeBtnPngData = tex.EncodeToPNG();

        // 清理
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        Destroy(tex);

        Debug.Log($"Close button loaded: {_closeBtnPngData.Length} bytes");
        
#if UNITY_ANDROID
        // 将 PNG 数据传递给 WebViewHelper
        if (_webViewHelper != null && _closeBtnPngData != null)
        {
            _webViewHelper.SetCloseButtonPngData(_closeBtnPngData);
        }
#endif
    }


    private void HandleBackPressed()
    {
        float now = Time.realtimeSinceStartup;
        if (now - _lastBackPressedTime <= BACK_PRESS_THRESHOLD)
        {
            Application.Quit();
            return;
        }

        _lastBackPressedTime = now;
        ShowExitTip();

        if (_clearCoroutine != null)
            StopCoroutine(_clearCoroutine);
        _clearCoroutine = StartCoroutine(ClearTipAfterDelay(BACK_PRESS_THRESHOLD));
    }

    private void ShowExitTip()
    {
        if (displayText != null)
        {
            displayText.text = EXIT_TIP;
            displayText.gameObject.SetActive(true);
        }
#if UNITY_ANDROID
        else
        {
            ShowAndroidToast(EXIT_TIP);
        }
#endif
    }

    private IEnumerator ClearTipAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (displayText != null)
        {
            displayText.text = "";
            displayText.gameObject.SetActive(false);
        }
        _lastBackPressedTime = 0f;
        _clearCoroutine = null;
    }

    public void OpenWebUrl()
    {
        if (urlInputField != null)
        {
            OpenFloatingWebView(urlInputField.text);
        }
    }

#if UNITY_ANDROID
    private void ShowAndroidToast(string msg)
    {
        try
        {
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            if (activity == null) return;

            AndroidJavaClass toastClass = new AndroidJavaClass("android.widget.Toast");
            activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                AndroidJavaObject context = activity.Call<AndroidJavaObject>("getApplicationContext");
                AndroidJavaObject toast = toastClass.CallStatic<AndroidJavaObject>("makeText", context, msg, 0);
                toast.Call("show");
            }));
        }
        catch { /* 忽略 Toast 显示错误 */ }
    }
#endif

    private void OpenFloatingWebView(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogWarning("URL is empty");
            return;
        }

        // 添加协议前缀
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
        {
            url = "https://" + url;
        }

        Debug.Log("Opening WebView: " + url);

#if UNITY_ANDROID && !UNITY_EDITOR
        if (_webViewHelper != null)
        {
            _webViewHelper.OpenWebView(url);
        }
        else
        {
            Debug.LogError("WebViewHelper is not initialized");
        }
#elif UNITY_STANDALONE_WIN
        OpenWinFormsBrowser(url);
#endif
    }
    
    #if UNITY_STANDALONE_WIN
    private static void OpenWinFormsBrowser(string url)
    {
    }
#endif
}