using System;
using System.Collections;
using NativeWebSocket;
using UnityEngine;
using RTSP.Data;

namespace RTSP
{
    public class WebSocketManager : MonoBehaviour
    {
        public event Action OnConnected;
        public event Action<string> OnMessageReceived;
        public event Action<WebSocketCloseCode> OnDisconnected;
        public event Action<string> OnError;
        
        private WebSocket ws;
        private RTSPConfig config;
        
        public WebSocketState? State => ws?.State;
        
        public void Initialize(RTSPConfig config)
        {
            this.config = config;
        }
        
        public IEnumerator Connect()
        {
            Debug.Log("Initializing WebSocket...");
            ws = new WebSocket(config.websocketUrl);
            
            ws.OnOpen += () => {
                Debug.Log("WebSocket connection opened");
                OnConnected?.Invoke();
            };
            ws.OnError += (e) => {
                Debug.LogError("WebSocket error: " + e);
                OnError?.Invoke(e);
            };
            ws.OnClose += (e) => {
                Debug.Log("WebSocket closed with code: " + e);
                OnDisconnected?.Invoke(e);
            };
            ws.OnMessage += (bytes) => {
                string message = System.Text.Encoding.UTF8.GetString(bytes);
                Debug.Log("WS Received: " + message);
                OnMessageReceived?.Invoke(message);
            };
            
            Debug.Log("Connecting WebSocket...");
            ws.Connect();
            
            float timeout = config.connectionTimeout;
            while (ws.State == WebSocketState.Connecting && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            
            if (ws.State != WebSocketState.Open)
            {
                Debug.LogError("WebSocket connection failed or timed out.");
            }
            else
            {
                Debug.Log("WebSocket connected.");
            }
        }
        
        // メソッド名を変更（SendMessage → Send）
        public void Send(string message)
        {
            if (ws?.State == WebSocketState.Open)
            {
                ws.SendText(message);
            }
        }
        
        public void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            ws?.DispatchMessageQueue();
#endif
        }
        
        public void Disconnect()
        {
            if (ws != null)
            {
                Debug.Log("Cleaning up WebSocket");
                ws.Close();
                ws = null;
            }
        }
    }
}