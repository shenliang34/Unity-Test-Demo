using System;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Streaming POST helper using UnityWebRequest with a DownloadHandlerScript.
/// Supports timeout and cancellation.
/// </summary>
public class UnityWebRequestStreamer : MonoBehaviour
{
    private const float DEFAULT_TIMEOUT = 30f;

    private UnityWebRequest _currentRequest;
    private Coroutine _currentCoroutine;
    private float _startTime;
    private bool _isCancelledByUser;

    /// <summary>
    /// Initiate a streaming POST request.
    /// </summary>
    public Coroutine PostStream(string url, string jsonBody,
                                Action<string> onChunk,
                                Action onComplete = null,
                                Action<string> onError = null,
                                float timeoutSeconds = DEFAULT_TIMEOUT)
    {
        if (_currentCoroutine != null)
            StopCoroutine(_currentCoroutine);
        
        _currentCoroutine = StartCoroutine(PostStreamRoutine(url, jsonBody, onChunk, onComplete, onError, timeoutSeconds));
        return _currentCoroutine;
    }

    /// <summary>
    /// Cancel the current request.
    /// </summary>
    public void CancelCurrent()
    {
        _isCancelledByUser = true;
        if (_currentCoroutine != null)
        {
            StopCoroutine(_currentCoroutine);
            _currentCoroutine = null;
        }
    }

    private IEnumerator PostStreamRoutine(string url, string jsonBody,
                                          Action<string> onChunk,
                                          Action onComplete,
                                          Action<string> onError,
                                          float timeoutSeconds)
    {
        _isCancelledByUser = false;
        var downloadHandler = new StreamingDownloadHandler(onChunk, this);
        _startTime = Time.realtimeSinceStartup;

        using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            _currentRequest = request;
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody ?? string.Empty);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.uploadHandler.contentType = "application/json";
            request.downloadHandler = downloadHandler;
            request.SetRequestHeader("Accept", "*/*");

            var op = request.SendWebRequest();
            while (!op.isDone)
            {
                // Timeout check
                float elapsed = Time.realtimeSinceStartup - _startTime;
                if (elapsed > timeoutSeconds)
                {
                    CancelCurrent();
                    onError?.Invoke("Request timed out");
                    yield break;
                }
                yield return null;
            }

            // Check for errors
            if (IsRequestError(request))
            {
                if (!_isCancelledByUser && !request.error.Contains("Failure writing output to destination"))
                {
                    onError?.Invoke(request.error);
                }
            }
            else
            {
                onComplete?.Invoke();
            }

            // Cleanup
            request.Dispose();
            _currentRequest = null;
            _currentCoroutine = null;
            _isCancelledByUser = false;
        }
    }

    private bool IsRequestError(UnityWebRequest request)
    {
#if UNITY_2020_1_OR_NEWER
        return request.result == UnityWebRequest.Result.ConnectionError || 
               request.result == UnityWebRequest.Result.ProtocolError;
#else
        return request.isNetworkError || request.isHttpError;
#endif
    }

    /// <summary>
    /// Custom DownloadHandlerScript that processes data chunks incrementally with UTF-8 decoding.
    /// </summary>
    private class StreamingDownloadHandler : DownloadHandlerScript
    {
        private readonly Action<string> _onChunk;
        private readonly Decoder _utf8Decoder;
        private readonly UnityWebRequestStreamer _owner;

        public StreamingDownloadHandler(Action<string> onChunk, UnityWebRequestStreamer owner) : base()
        {
            _onChunk = onChunk;
            _utf8Decoder = Encoding.UTF8.GetDecoder();
            _owner = owner;
        }

        /// <summary>
        /// Called when data is received from the server.
        /// </summary>
        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            // Terminate if cancelled
            if (_owner._isCancelledByUser)
                return false;

            if (data == null || dataLength == 0)
                return true;

            try
            {
                char[] chars = new char[dataLength];
                int charCount = _utf8Decoder.GetChars(data, 0, dataLength, chars, 0, false);
                if (charCount > 0)
                {
                    string chunk = new string(chars, 0, charCount);
                    try
                    {
                        _onChunk?.Invoke(chunk);
                    }
                    catch
                    {
                        // Ignore callback exceptions
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Called when download completes. Flushes any remaining characters from the decoder.
        /// </summary>
        protected override void CompleteContent()
        {
            try
            {
                char[] flushChars = new char[4 * 1024];
                int count = _utf8Decoder.GetChars(Array.Empty<byte>(), 0, 0, flushChars, 0, true);
                if (count > 0)
                {
                    string last = new string(flushChars, 0, count);
                    try
                    {
                        _onChunk?.Invoke(last);
                    }
                    catch
                    {
                        // Ignore callback exceptions
                    }
                }
            }
            catch
            {
                // Ignore decoder errors
            }
        }
    }

    private void OnDestroy()
    {
        CancelCurrent();
    }
}
