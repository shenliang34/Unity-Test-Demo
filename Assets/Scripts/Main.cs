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
    private void Awake()
    {
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
        const string unityTag = "UnityWebView";
        int buttonSizePx = Mathf.RoundToInt(48f * Screen.dpi / 160f); // ⭐ 提前计算
        activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
        {
            AndroidJavaClass logClass = new AndroidJavaClass("android.util.Log");
            logClass.CallStatic<int>("d", unityTag, "Opening WebView dialog for URL: " + url);
            
            // 使用全屏主题: android.R.style.Theme_Black_NoTitleBar_Fullscreen
            AndroidJavaClass androidR = new AndroidJavaClass("android.R$style");
            int fullscreenTheme = androidR.GetStatic<int>("Theme_Black_NoTitleBar_Fullscreen");
            AndroidJavaObject dialog = new AndroidJavaObject("android.app.Dialog", activity, fullscreenTheme);
            
            logClass.CallStatic<int>("d", unityTag, "Dialog created with fullscreen theme.");
            // 创建主布局 LinearLayout
            AndroidJavaObject linearLayout = new AndroidJavaObject("android.widget.LinearLayout", activity);
            linearLayout.Call("setOrientation", 1); // VERTICAL = 1

            // 创建关闭按钮
            AndroidJavaObject closeButton = new AndroidJavaObject("android.widget.Button", activity);
            closeButton.Call("setText", "关闭");
            logClass.CallStatic<int>("d", unityTag, "Close button created.");
            
            // 创建 WebView
            AndroidJavaObject webView = new AndroidJavaObject("android.webkit.WebView", activity);
            
            logClass.CallStatic<int>("d", unityTag, "WebView created.");
            // 获取 WebSettings 并配置
            AndroidJavaObject webSettings = webView.Call<AndroidJavaObject>("getSettings");
            webSettings.Call("setJavaScriptEnabled", true);
            webSettings.Call("setDomStorageEnabled", true);
            webSettings.Call("setLoadWithOverviewMode", true);
            webSettings.Call("setUseWideViewPort", true);
            webSettings.Call("setSupportZoom", true);
            webSettings.Call("setBuiltInZoomControls", true);
            webSettings.Call("setDisplayZoomControls", false);
            
            logClass.CallStatic<int>("d", unityTag, "WebView settings configured.");

            // 设置 WebViewClient 防止跳转到外部浏览器
            AndroidJavaObject webViewClient = new AndroidJavaObject("android.webkit.WebViewClient");
            webView.Call("setWebViewClient", webViewClient);
            
            logClass.CallStatic<int>("d", unityTag, "WebViewClient set.");

            // 加载 URL
            webView.Call("loadUrl", url);
            logClass.CallStatic<int>("d", unityTag, "URL loaded in WebView: " + url);

            // 设置布局参数
            int matchParent = -1; // ViewGroup.LayoutParams.MATCH_PARENT
            int wrapContent = -2; // ViewGroup.LayoutParams.WRAP_CONTENT

            // 关闭按钮的布局参数
            // AndroidJavaObject buttonParams = new AndroidJavaObject(
            //     "android.widget.LinearLayout$LayoutParams", matchParent, wrapContent);
            // linearLayout.Call("addView", closeButton, buttonParams);
            // 创建 FrameLayout 作为主容器
            AndroidJavaObject frameLayout = new AndroidJavaObject("android.widget.FrameLayout", activity);
            logClass.CallStatic<int>("d", unityTag, "Close button added to layout.");
            // WebView 的布局参数
            AndroidJavaObject webViewParams = new AndroidJavaObject(
                "android.widget.FrameLayout$LayoutParams", matchParent, matchParent);
            frameLayout.Call("addView", webView, webViewParams);
            logClass.CallStatic<int>("d", unityTag, "Close button PNG data length: " + (_closeBtnPngData != null ? _closeBtnPngData.Length : 0));
            if (_closeBtnPngData != null && _closeBtnPngData.Length > 0)
            {
                //创建 ImageButton 并设置图片
                AndroidJavaObject imageButton = new AndroidJavaObject("android.widget.ImageButton", activity);
                
                // 创建 BitmapFactory
                AndroidJavaClass bitmapFactory = new AndroidJavaClass("android.graphics.BitmapFactory");
                AndroidJavaObject bitmap = bitmapFactory.CallStatic<AndroidJavaObject>("decodeByteArray", _closeBtnPngData, 0, _closeBtnPngData.Length);
                
                if (bitmap != null)
                {
                    //设置图片
                    imageButton.Call("setImageBitmap", bitmap);
                }
                logClass.CallStatic<int>("d", unityTag, "Close button image set from PNG data.");
                
                // 去掉默认背景
                AndroidJavaClass colorClass = new AndroidJavaClass("android.graphics.Color");
                int transparent = colorClass.GetStatic<int>("TRANSPARENT");
                imageButton.Call("setBackgroundColor", transparent);
                
                // 去掉内边距
                imageButton.Call("setPadding", 0, 0, 0, 0);
                
                // 设置明确的尺寸（例如 48dp）
                AndroidJavaObject imageBtnParams = new AndroidJavaObject(
                    "android.widget.FrameLayout$LayoutParams", buttonSizePx, buttonSizePx);
                // 设置 Gravity 为右上角 (TOP | END)
                AndroidJavaClass gravityClass = new AndroidJavaClass("android.view.Gravity");
                int gravityTopEnd = gravityClass.GetStatic<int>("TOP") | gravityClass.GetStatic<int>("END");
                imageBtnParams.Set<int>("gravity", gravityTopEnd);
                
                // linearLayout.Call("addView", imageButton, imageBtnParams);
                frameLayout.Call("addView", imageButton, imageBtnParams);
                //打印sizePx
                logClass.CallStatic<int>("d", unityTag, "Close button size set to: " + buttonSizePx + "px");
                
                // 设置点击事件
                imageButton.Call("setOnClickListener", new ViewOnClickListener(() => {
                    webView.Call("destroy");
                    dialog.Call("dismiss");
                }));
            }   

            // WebView 的布局参数 (权重为1，填充剩余空间)
            // AndroidJavaObject webViewParams = new AndroidJavaObject(
            //     "android.widget.LinearLayout$LayoutParams", matchParent, 0, 1.0f);
            // linearLayout.Call("addView", webView, webViewParams);
            //
            // // 设置关闭按钮点击事件
            // closeButton.Call("setOnClickListener", new ViewOnClickListener(() => {
            //     webView.Call("destroy");
            //     dialog.Call("dismiss");
            // }));

            // 设置 Dialog 内容
            // dialog.Call("setContentView", linearLayout);
            // 设置 Dialog 内容为 FrameLayout
            dialog.Call("setContentView", frameLayout);
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