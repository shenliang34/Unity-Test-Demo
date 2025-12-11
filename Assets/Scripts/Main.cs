using System.Collections;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

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
    
    private void OpenAndroidWebViewInternal(string url, AndroidJavaObject activity)
    {
        activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
        {
            // 创建一个全屏的 Dialog
            AndroidJavaClass dialogClass = new AndroidJavaClass("android.app.Dialog");
            // 使用全屏主题: android.R.style.Theme_Black_NoTitleBar_Fullscreen
            AndroidJavaClass androidR = new AndroidJavaClass("android.R$style");
            int fullscreenTheme = androidR.GetStatic<int>("Theme_Black_NoTitleBar_Fullscreen");
            AndroidJavaObject dialog = new AndroidJavaObject("android.app.Dialog", activity, fullscreenTheme);

            // 创建主布局 LinearLayout
            AndroidJavaObject linearLayout = new AndroidJavaObject("android.widget.LinearLayout", activity);
            linearLayout.Call("setOrientation", 1); // VERTICAL = 1

            // 创建关闭按钮
            AndroidJavaObject closeButton = new AndroidJavaObject("android.widget.Button", activity);
            closeButton.Call("setText", "关闭");
            
            // 创建 WebView
            AndroidJavaObject webView = new AndroidJavaObject("android.webkit.WebView", activity);
            
            // 获取 WebSettings 并配置
            AndroidJavaObject webSettings = webView.Call<AndroidJavaObject>("getSettings");
            webSettings.Call("setJavaScriptEnabled", true);
            webSettings.Call("setDomStorageEnabled", true);
            webSettings.Call("setLoadWithOverviewMode", true);
            webSettings.Call("setUseWideViewPort", true);
            webSettings.Call("setSupportZoom", true);
            webSettings.Call("setBuiltInZoomControls", true);
            webSettings.Call("setDisplayZoomControls", false);

            // 设置 WebViewClient 防止跳转到外部浏览器
            AndroidJavaObject webViewClient = new AndroidJavaObject("android.webkit.WebViewClient");
            webView.Call("setWebViewClient", webViewClient);

            // 加载 URL
            webView.Call("loadUrl", url);

            // 设置布局参数
            int matchParent = -1; // ViewGroup.LayoutParams.MATCH_PARENT
            int wrapContent = -2; // ViewGroup.LayoutParams.WRAP_CONTENT

            // 关闭按钮的布局参数
            AndroidJavaObject buttonParams = new AndroidJavaObject(
                "android.widget.LinearLayout$LayoutParams", matchParent, wrapContent);
            linearLayout.Call("addView", closeButton, buttonParams);

            // WebView 的布局参数 (权重为1，填充剩余空间)
            AndroidJavaObject webViewParams = new AndroidJavaObject(
                "android.widget.LinearLayout$LayoutParams", matchParent, 0, 1.0f);
            linearLayout.Call("addView", webView, webViewParams);

            // 设置关闭按钮点击事件
            closeButton.Call("setOnClickListener", new ViewOnClickListener(() => {
                webView.Call("destroy");
                dialog.Call("dismiss");
            }));

            // 设置 Dialog 内容
            dialog.Call("setContentView", linearLayout);
            dialog.Call("setCancelable", true);

            // 获取窗口并设置全屏
            AndroidJavaObject window = dialog.Call<AndroidJavaObject>("getWindow");
            if (window != null)
            {
                // 设置背景为白色
                AndroidJavaClass colorClass = new AndroidJavaClass("android.graphics.Color");
                int whiteColor = colorClass.GetStatic<int>("WHITE");
                AndroidJavaObject colorDrawable = new AndroidJavaObject("android.graphics.drawable.ColorDrawable", whiteColor);
                window.Call("setBackgroundDrawable", colorDrawable);
                
                // 设置窗口标志为全屏
                AndroidJavaClass windowManagerLayoutParams = new AndroidJavaClass("android.view.WindowManager$LayoutParams");
                int flagFullscreen = windowManagerLayoutParams.GetStatic<int>("FLAG_FULLSCREEN");
                window.Call("setFlags", flagFullscreen, flagFullscreen);
            }

            // 显示对话框
            dialog.Call("show");
            
            // 在 show() 之后设置窗口大小以确保全屏
            if (window != null)
            {
                window.Call("setLayout", matchParent, matchParent);
            }
        }));
    }

    // View.OnClickListener 实现类
    private class ViewOnClickListener : AndroidJavaProxy
    {
        private System.Action _onClick;

        public ViewOnClickListener(System.Action onClick) 
            : base("android.view.View$OnClickListener")
        {
            _onClick = onClick;
        }

        public void onClick(AndroidJavaObject view)
        {
            Debug.Log("Close button clicked");
            _onClick?.Invoke();
        }
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
        try
        {
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            if (activity != null)
            {
                OpenAndroidWebViewInternal(url, activity);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error opening WebView: " + e.Message);
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