using UnityEngine;

#if UNITY_ANDROID
/// <summary>
/// Android WebView 辅助类，用于在 Unity 中打开全屏 WebView
/// </summary>
public class AndroidWebViewHelper
{
    private byte[] _closeBtnPngData;

    /// <summary>
    /// 设置关闭按钮的 PNG 数据
    /// </summary>
    /// <param name="pngData">PNG 格式的图片数据</param>
    public void SetCloseButtonPngData(byte[] pngData)
    {
        _closeBtnPngData = pngData;
    }

    /// <summary>
    /// 打开全屏 WebView
    /// </summary>
    /// <param name="url">要加载的 URL</param>
    public void OpenWebView(string url)
    {
        try
        {
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            if (activity != null)
            {
                OpenWebViewInternal(url, activity);
            }
            else
            {
                Debug.LogError("Cannot get Android activity");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error opening WebView: " + e.Message);
        }
    }

    private void OpenWebViewInternal(string url, AndroidJavaObject activity)
    {
        const string unityTag = "UnityWebView";
        int buttonSizePx = Mathf.RoundToInt(48f * Screen.dpi / 160f);
        int marginPx = Mathf.RoundToInt(16f * Screen.dpi / 160f);
        
        activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
        {
            AndroidJavaClass logClass = new AndroidJavaClass("android.util.Log");
            logClass.CallStatic<int>("d", unityTag, "Opening WebView dialog for URL: " + url);
            
            // 使用全屏主题
            AndroidJavaClass androidR = new AndroidJavaClass("android.R$style");
            int fullscreenTheme = androidR.GetStatic<int>("Theme_Black_NoTitleBar_Fullscreen");
            AndroidJavaObject dialog = new AndroidJavaObject("android.app.Dialog", activity, fullscreenTheme);
            
            logClass.CallStatic<int>("d", unityTag, "Dialog created with fullscreen theme.");
            
            // 创建 FrameLayout 作为主容器
            AndroidJavaObject frameLayout = new AndroidJavaObject("android.widget.FrameLayout", activity);

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
            
            // 先添加 WebView 到 FrameLayout (填充整个容器)
            AndroidJavaObject webViewParams = new AndroidJavaObject(
                "android.widget.FrameLayout$LayoutParams", matchParent, matchParent);
            frameLayout.Call("addView", webView, webViewParams);
            logClass.CallStatic<int>("d", unityTag, "WebView added to FrameLayout.");
            
            // 如果有关闭按钮图片数据，创建 ImageButton
            logClass.CallStatic<int>("d", unityTag, "Close button PNG data length: " + (_closeBtnPngData != null ? _closeBtnPngData.Length : 0));
            if (_closeBtnPngData != null && _closeBtnPngData.Length > 0)
            {
                // 创建 ImageButton 并设置图片
                AndroidJavaObject imageButton = new AndroidJavaObject("android.widget.ImageButton", activity);
                
                // 创建 BitmapFactory
                AndroidJavaClass bitmapFactory = new AndroidJavaClass("android.graphics.BitmapFactory");
                AndroidJavaObject bitmap = bitmapFactory.CallStatic<AndroidJavaObject>("decodeByteArray", _closeBtnPngData, 0, _closeBtnPngData.Length);
                
                if (bitmap != null)
                {
                    // 设置图片
                    imageButton.Call("setImageBitmap", bitmap);
                }
                logClass.CallStatic<int>("d", unityTag, "Close button image set from PNG data.");
                
                // 去掉默认背景
                AndroidJavaClass colorClass = new AndroidJavaClass("android.graphics.Color");
                int transparent = colorClass.GetStatic<int>("TRANSPARENT");
                imageButton.Call("setBackgroundColor", transparent);
                
                // 去掉内边距
                imageButton.Call("setPadding", 0, 0, 0, 0);
                
                // ImageButton 的布局参数 - 设置在右上角
                AndroidJavaObject imageBtnParams = new AndroidJavaObject(
                    "android.widget.FrameLayout$LayoutParams", wrapContent, wrapContent);
                
                // 设置 Gravity 为右上角 (TOP | END)
                AndroidJavaClass gravityClass = new AndroidJavaClass("android.view.Gravity");
                int gravityTopEnd = gravityClass.GetStatic<int>("TOP") | gravityClass.GetStatic<int>("END");
                imageBtnParams.Set<int>("gravity", gravityTopEnd);
                
                // 设置边距
                imageBtnParams.Call("setMargins", marginPx, marginPx, marginPx, marginPx);
                
                // 添加按钮到 FrameLayout (后添加,显示在 WebView 上层)
                frameLayout.Call("addView", imageButton, imageBtnParams);
                logClass.CallStatic<int>("d", unityTag, "Close button size set to: " + buttonSizePx + "px");
                
                // 设置点击事件
                imageButton.Call("setOnClickListener", new ViewOnClickListener(() => {
                    webView.Call("destroy");
                    dialog.Call("dismiss");
                }));
            }

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
}
#endif

