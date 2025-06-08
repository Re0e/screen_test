using System.Collections;
using UnityEngine;
using Unity.WebRTC;
using RTSP.Data;
using Utils;

namespace RTSP
{
    public class RTSPVideoReceiver : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private RTSPConfig config;
        
        private WebSocketManager webSocketManager;
        private WebRTCManager webRTCManager;
        private VideoDisplayManager videoDisplayManager;
        
        private void Start()
        {
            StartCoroutine(Initialize());
        }
        
        private IEnumerator Initialize()
        {
            // コーデック確認
            if (!CodecChecker.CheckH264Support())
            {
                Debug.LogError("H.264 codec not supported!");
                yield break;
            }
            
            // マネージャー初期化
            InitializeManagers();
            
            // 接続開始
            yield return StartCoroutine(ConnectAndSetup());
        }
        
        private void InitializeManagers()
        {
            webSocketManager = gameObject.AddComponent<WebSocketManager>();
            webRTCManager = gameObject.AddComponent<WebRTCManager>();
            videoDisplayManager = gameObject.AddComponent<VideoDisplayManager>();
            
            webSocketManager.Initialize(config);
            webRTCManager.Initialize(config);
            videoDisplayManager.Initialize(config);
            
            // イベント接続
            webSocketManager.OnMessageReceived += HandleWebSocketMessage;
            webRTCManager.OnVideoTrackReceived += videoDisplayManager.SetVideoTrack;
            webRTCManager.OnIceCandidate += HandleIceCandidate;
            webRTCManager.OnOfferCreated += HandleOfferCreated;
        }
        
        private IEnumerator ConnectAndSetup()
        {
            yield return StartCoroutine(webSocketManager.Connect());
            yield return StartCoroutine(webRTCManager.SetupPeerConnection());
            yield return StartCoroutine(webRTCManager.CreateOffer());
        }
        
        private void HandleOfferCreated(SdpMessage offer)
        {
            Debug.Log($"Sending SDP offer: {JsonUtility.ToJson(offer)}");
            webSocketManager.Send("sdp:" + JsonUtility.ToJson(offer));
        }
        
        private void HandleWebSocketMessage(string message)
        {
            StartCoroutine(ProcessMessage(message));
        }
        
        private IEnumerator ProcessMessage(string message)
        {
            if (message.StartsWith("sdp:"))
            {
                string sdpJson = message.Substring(4);
                var sdpMsg = JsonUtility.FromJson<SdpMessage>(sdpJson);
                yield return StartCoroutine(webRTCManager.SetRemoteDescription(sdpMsg));
            }
            else if (message.StartsWith("ice:"))
            {
                string iceJson = message.Substring(4);
                var ice = JsonUtility.FromJson<IceCandidateMessage>(iceJson);
                webRTCManager.AddIceCandidate(ice);
            }
        }
        
        private void HandleIceCandidate(RTCIceCandidate candidate)
        {
            if (candidate != null)
            {
                IceCandidateMessage msg = new IceCandidateMessage
                {
                    candidate = candidate.Candidate,
                    sdpMid = candidate.SdpMid,
                    sdpMLineIndex = candidate.SdpMLineIndex ?? 0
                };
                Debug.Log($"Sending ICE candidate JSON: {JsonUtility.ToJson(msg)}");
                webSocketManager.Send("ice:" + JsonUtility.ToJson(msg));
            }
        }
        
        private void Update()
        {
            webSocketManager?.Update();
            videoDisplayManager?.Update();
        }
        
        private void OnDestroy()
        {
            webSocketManager?.Disconnect();
            webRTCManager?.Dispose();
        }
        
        void OnGUI()
        {
            if (config == null || !config.showOnScreenDebug) return;
            
            GUILayout.BeginArea(new Rect(10, 10, 350, 150));
            GUILayout.Label($"WebSocket State: {webSocketManager?.State?.ToString() ?? "None"}");
            GUILayout.Label($"PeerConnection State: {webRTCManager?.ConnectionState?.ToString() ?? "None"}");
            GUILayout.Label($"Video Receiving: {videoDisplayManager?.IsReceiving ?? false}");
            if (videoDisplayManager?.VideoTrack?.Texture != null)
            {
                var texture = videoDisplayManager.VideoTrack.Texture;
                GUILayout.Label($"Video Texture: {texture.width}x{texture.height}");
            }
            GUILayout.EndArea();
        }
    }
}