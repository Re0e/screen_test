using System;
using System.Collections;
using UnityEngine;
using NativeWebSocket;
using Unity.WebRTC;

[Serializable]
public class SdpMessage
{
    public string type; // stringで渡す
    public string sdp;

    public SdpMessage(RTCSessionDescription desc)
    {
        type = desc.type.ToString().ToLower();  // enumを小文字文字列に変換
        sdp = desc.sdp;
    }
}

[Serializable]
public class IceCandidateMessage
{
    public string candidate;     // ICE candidateの文字列
    public string sdpMid;        // media stream identification
    public int sdpMLineIndex;    // media line index
}

public class WebRTCTest : MonoBehaviour
{
    private WebSocket ws;
    private RTCPeerConnection pc;
    private RTCDataChannel dataChannel;

    void Start()
    {
        Debug.Log("Start SetupConnection");
        StartCoroutine(SetupConnection());
    }

    private IEnumerator SetupConnection()
    {
        Debug.Log("Initializing WebSocket...");
        ws = new WebSocket("ws://localhost:8080/ws");

        ws.OnOpen += () =>
        {
            Debug.Log("WebSocket connection opened");
        };

        ws.OnError += (e) =>
        {
            Debug.LogError("WebSocket error: " + e);
        };

        ws.OnClose += (e) =>
        {
            Debug.Log("WebSocket closed with code: " + e);
        };

        ws.OnMessage += (bytes) =>
        {
            string message = System.Text.Encoding.UTF8.GetString(bytes);
            Debug.Log("WS Received: " + message);
            StartCoroutine(OnSignalingMessage(message));
        };

        yield return StartCoroutine(ConnectWebSocket());

        Debug.Log("Creating PeerConnection...");
        var config = GetSelectedSdpSemantics();
        pc = new RTCPeerConnection(ref config);
        Debug.Log("PeerConnection created");

        pc.OnIceCandidate = candidate =>
        {
            if (candidate == null) return;

            IceCandidateMessage msg = new IceCandidateMessage()
            {
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex ?? 0 // null安全に0を代入
            };

            string candidateJson = JsonUtility.ToJson(msg);

            Debug.Log("Sending ICE candidate JSON: " + candidateJson);

            ws.SendText("ice:" + candidateJson);
        };

        pc.OnIceConnectionChange = state =>
        {
            Debug.Log("ICE connection state changed: " + state);
        };

        Debug.Log("Creating DataChannel...");
        var options = new RTCDataChannelInit();
        dataChannel = pc.CreateDataChannel("chat", options);

        dataChannel.OnOpen += () =>
        {
            Debug.Log("DataChannel opened.");
            dataChannel.Send("Hello from Unity!");
        };

        dataChannel.OnMessage += (bytes) =>
        {
            string msg = System.Text.Encoding.UTF8.GetString(bytes);
            Debug.Log("DataChannel Received: " + msg);
        };

        Debug.Log("Creating offer...");
        var offerOp = pc.CreateOffer();
        yield return offerOp;
        Debug.Log("Offer created");

        var desc = offerOp.Desc;

        Debug.Log("Setting local description...");
        var localOp = pc.SetLocalDescription(ref desc);
        yield return localOp;
        Debug.Log("Local description set");

        SdpMessage sdpMsg = new SdpMessage(desc);
        string sdpJson = JsonUtility.ToJson(sdpMsg);
        Debug.Log("Sending SDP offer");
        ws.SendText("sdp:" + sdpJson);
    }

    private IEnumerator OnSignalingMessage(string message)
    {
        if (message.StartsWith("sdp:"))
        {
            string sdpJson = message.Substring(4);
            Debug.Log("Received SDP message");

            var desc = JsonUtility.FromJson<RTCSessionDescription>(sdpJson);

            Debug.Log("Setting remote description...");
            var remoteOp = pc.SetRemoteDescription(ref desc);
            yield return remoteOp;
            Debug.Log("Remote description set");
        }
        else if (message.StartsWith("ice:"))
        {
            string iceJson = message.Substring(4);
            Debug.Log("Received ICE candidate: " + iceJson);

            var ice = JsonUtility.FromJson<RTCIceCandidateInit>(iceJson);
            try
            {
                pc.AddIceCandidate(new RTCIceCandidate(ice));
                Debug.Log("Added ICE candidate");
            }
            catch (Exception ex)
            {
                Debug.LogError("AddIceCandidate failed: " + ex.Message);
            }
        }
        else
        {
            Debug.LogWarning("Unknown signaling message: " + message);
        }

        yield return null;
    }

    private IEnumerator ConnectWebSocket()
    {
        bool connected = false;
        ws.OnOpen += () => { connected = true; };

        Debug.Log("Connecting WebSocket...");
        ws.Connect();

        float timeout = 5f;
        while (!connected && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (!connected)
        {
            Debug.LogError("WebSocket connection timed out.");
        }
        else
        {
            Debug.Log("WebSocket connected.");
        }
    }

    private RTCConfiguration GetSelectedSdpSemantics()
    {
        return new RTCConfiguration
        {
            iceServers = new[]
            {
                new RTCIceServer
                {
                    urls = new[] { "stun:stun.l.google.com:19302" }
                }
            }
        };
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        ws?.DispatchMessageQueue();
#endif
    }

    void OnDestroy()
    {
        Debug.Log("Cleaning up WebRTC and WebSocket");
        dataChannel?.Close();
        pc?.Close();
        ws?.Close();
    }
}
