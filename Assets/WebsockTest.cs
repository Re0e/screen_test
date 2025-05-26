using System;
using System.Text;
using UnityEngine;
using NativeWebSocket;

public class WebSocketTest : MonoBehaviour
{
    private WebSocket websocket;

    async void Start()
    {
        websocket = new WebSocket("ws://localhost:8080/ws");

        websocket.OnOpen += () =>
        {
            Debug.Log("Connection open!");
        };

        websocket.OnError += (e) =>
        {
            Debug.Log("Error! " + e);
        };

        websocket.OnClose += (e) =>
        {
            Debug.Log("Connection closed!");
        };

        websocket.OnMessage += (bytes) =>
        {
            var message = Encoding.UTF8.GetString(bytes);
            Debug.Log("Message from server: " + message);
        };

        await websocket.Connect();
    }

    async void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
#endif

        if (Input.GetKeyDown(KeyCode.Space))
        {
            string message = "Hello from Unity";
            await websocket.SendText(message);
            Debug.Log("Sent: " + message);
        }
    }

    private async void OnApplicationQuit()
    {
        await websocket.Close();
    }
}
