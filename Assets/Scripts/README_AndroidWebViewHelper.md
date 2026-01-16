# AndroidWebViewHelper 使用说明

## 概述
`AndroidWebViewHelper` 是一个独立的 Android WebView 辅助类，可以在 Unity 项目中打开全屏的 WebView 对话框。该类已完全封装，便于移植到其他项目。

## 特性
- ✅ 全屏显示 WebView
- ✅ 自定义关闭按钮（支持 PNG 图片）
- ✅ 按钮浮动在右上角
- ✅ 支持 JavaScript 和 DOM 存储
- ✅ 支持缩放和手势操作
- ✅ 防止跳转到外部浏览器

## 使用方法

### 1. 创建实例
```csharp
#if UNITY_ANDROID
private AndroidWebViewHelper _webViewHelper;
#endif

private void Awake()
{
#if UNITY_ANDROID
    _webViewHelper = new AndroidWebViewHelper();
#endif
}
```

### 2. 设置关闭按钮图片（可选）
```csharp
// 从 Resources 加载 Sprite 并转换为 PNG 数据
Sprite closeBtn = Resources.Load<Sprite>("Sprites/CloseBtn");
// ... 转换为 PNG byte[] ...
byte[] pngData = texture.EncodeToPNG();

// 设置到 WebViewHelper
_webViewHelper.SetCloseButtonPngData(pngData);
```

### 3. 打开 WebView
```csharp
string url = "https://www.example.com";
_webViewHelper.OpenWebView(url);
```

## 移植到其他项目

### 需要的文件
1. `AndroidWebViewHelper.cs` - 核心类文件

### 移植步骤
1. 将 `AndroidWebViewHelper.cs` 复制到目标项目的 Scripts 文件夹
2. 在需要使用的地方创建实例：
   ```csharp
   #if UNITY_ANDROID
   private AndroidWebViewHelper _webViewHelper = new AndroidWebViewHelper();
   #endif
   ```
3. 可选：设置自定义关闭按钮图片
4. 调用 `OpenWebView(url)` 打开 WebView

### 依赖项
- Unity 引擎（使用了 `UnityEngine` 命名空间）
- Android 平台（需要在 Android 设备或模拟器上运行）

### 注意事项
- 该类仅在 `UNITY_ANDROID` 宏定义下编译
- 如果不设置关闭按钮图片，WebView 将不显示关闭按钮（需要通过返回键或其他方式关闭）
- WebView 配置为全屏显示，占据整个屏幕
- 默认启用了 JavaScript 和 DOM 存储

## API 参考

### 方法

#### `void SetCloseButtonPngData(byte[] pngData)`
设置关闭按钮的 PNG 图片数据。

**参数:**
- `pngData`: PNG 格式的图片字节数组

#### `void OpenWebView(string url)`
打开全屏 WebView 并加载指定 URL。

**参数:**
- `url`: 要加载的网页地址

## 示例代码

```csharp
using UnityEngine;

public class WebViewTest : MonoBehaviour
{
#if UNITY_ANDROID
    private AndroidWebViewHelper _webViewHelper;
#endif

    private void Awake()
    {
#if UNITY_ANDROID
        _webViewHelper = new AndroidWebViewHelper();
        
        // 可选：设置关闭按钮
        Sprite closeBtn = Resources.Load<Sprite>("CloseButton");
        if (closeBtn != null)
        {
            byte[] pngData = GetPngDataFromSprite(closeBtn);
            _webViewHelper.SetCloseButtonPngData(pngData);
        }
#endif
    }

    public void OpenGoogle()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        _webViewHelper?.OpenWebView("https://www.google.com");
#endif
    }

    private byte[] GetPngDataFromSprite(Sprite sprite)
    {
        // 实现 Sprite 到 PNG 的转换
        // 参考 Main.cs 中的 GetPNGData() 方法
        return null;
    }
}
```

## 技术细节

### WebView 配置
- JavaScript: 启用
- DOM 存储: 启用
- 宽视口: 启用
- 缩放支持: 启用
- 缩放控件: 显示内置控件，隐藏显示控件

### 布局结构
```
Dialog (全屏主题)
└── FrameLayout
    ├── WebView (填充整个容器)
    └── ImageButton (浮动在右上角，可选)
```

### 关闭按钮布局
- 位置: 右上角
- Gravity: TOP | END
- 边距: 16dp (自动根据屏幕 DPI 计算)
- 背景: 透明
- 大小: 根据图片内容自适应 (WRAP_CONTENT)

## 版本历史
- v1.0 (2026-01-16): 初始版本，支持基本的 WebView 显示功能

