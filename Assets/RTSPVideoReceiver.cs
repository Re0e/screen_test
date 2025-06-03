using System;
using System.Collections;
using System.Linq; // 修正: System.Linqを追加
using UnityEngine;
using NativeWebSocket;
using Unity.WebRTC;

[Serializable]
public class SdpMessage
{
    public string type;
    public string sdp;

    public SdpMessage(RTCSessionDescription desc)
    {
        type = desc.type.ToString().ToLower();
        sdp = desc.sdp;
    }
}

[Serializable]
public class IceCandidateMessage
{
    public string candidate;
    public string sdpMid;
    public int sdpMLineIndex;
}

public class RTSPVideoReceiver : MonoBehaviour
{
    [Header("Video Display")]
    [SerializeField] private GameObject videoPlane;
    
    private WebSocket ws;
    private RTCPeerConnection pc;
    private VideoStreamTrack videoStreamTrack;
    private Material videoMaterial;
    private bool isVideoReceiving = false;

    void Start()
    {
        Debug.Log("Start RTSP Video Receiver Setup with H.264 support");
        
        // H.264コーデックサポート確認
        CheckH264Support();
        
        SetupVideoPlane();
        StartCoroutine(SetupConnection());
    }

    private void CheckH264Support()
    {
        try
        {
            var capabilities = RTCRtpSender.GetCapabilities(TrackKind.Video);
            if (capabilities?.codecs != null)
            {
                bool h264Found = capabilities.codecs.Any(codec => codec.mimeType.Contains("H264", StringComparison.OrdinalIgnoreCase));
                Debug.Log(h264Found ? "H.264 codec is available." : "H.264 codec not found.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to check codec capabilities: " + ex.Message);
        }
    }

    private void SetupVideoPlane()
    {
        if (videoPlane == null)
        {
            Debug.LogError("Video Plane is not assigned!");
            return;
        }

        var renderer = videoPlane.GetComponent<Renderer>();
        if (renderer != null)
        {
            videoMaterial = new Material(Shader.Find("Standard"));
            renderer.material = videoMaterial;
            Debug.Log("Video material created and assigned to plane");
        }
        else
        {
            Debug.LogError("Renderer not found on video plane!");
        }
    }

    private IEnumerator SetupConnection()
    {
        ws = new WebSocket("ws://localhost:8080/ws");

        ws.OnOpen += () => { Debug.Log("WebSocket connection opened."); };
        ws.OnError += (e) => { Debug.LogError("WebSocket error: " + e); };
        ws.OnClose += (e) => { Debug.Log("WebSocket closed."); };
        ws.OnMessage += (bytes) =>
        {
            string message = System.Text.Encoding.UTF8.GetString(bytes);
            StartCoroutine(OnSignalingMessage(message));
        };

        yield return StartCoroutine(ConnectWebSocket());

        var config = GetConfiguration();
        pc = new RTCPeerConnection(ref config); // 修正: 閉じ括弧を追加

        var transceiverInit = new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly };
        pc.AddTransceiver(TrackKind.Video, transceiverInit);

        pc.OnIceCandidate = candidate =>
        {
            if (candidate != null)
            {
                IceCandidateMessage msg = new IceCandidateMessage
                {
                    candidate = candidate.Candidate,
                    sdpMid = candidate.SdpMid,
                    sdpMLineIndex = candidate.SdpMLineIndex ?? 0
                };
                ws.SendText("ice:" + JsonUtility.ToJson(msg));
            }
        };

        pc.OnTrack = (RTCTrackEvent trackEvent) =>
        {
            if (trackEvent.Track.Kind == TrackKind.Video)
            {
                videoStreamTrack = trackEvent.Track as VideoStreamTrack;
                if (videoStreamTrack != null)
                {
                    StartCoroutine(SetupVideoDisplay());
                }
            }
        };

        var offerOp = pc.CreateOffer();
        yield return offerOp;

        if (!offerOp.IsError)
        {
            var desc = offerOp.Desc;
            var localOp = pc.SetLocalDescription(ref desc);
            yield return localOp;

            if (!localOp.IsError)
            {
                ws.SendText("sdp:" + JsonUtility.ToJson(new SdpMessage(desc)));
            }
        }
    } // 修正: 閉じ括弧を追加

    private IEnumerator OnSignalingMessage(string message)
    {
        if (message.StartsWith("sdp:"))
        {
            string sdpJson = message.Substring(4);
            var sdpMsg = JsonUtility.FromJson<SdpMessage>(sdpJson);

            RTCSessionDescription desc = new RTCSessionDescription
            {
                type = Enum.TryParse(sdpMsg.type, true, out RTCSdpType type) ? type : RTCSdpType.Offer,
                sdp = sdpMsg.sdp
            };

            var remoteOp = pc.SetRemoteDescription(ref desc);
            yield return remoteOp;

            if (remoteOp.IsError)
            {
                Debug.LogError("SetRemoteDescription failed: " + remoteOp.Error.message);
            }
        }
        else if (message.StartsWith("ice:"))
        {
            string iceJson = message.Substring(4);
            var ice = JsonUtility.FromJson<IceCandidateMessage>(iceJson);

            RTCIceCandidateInit candidateInit = new RTCIceCandidateInit
            {
                candidate = ice.candidate,
                sdpMid = ice.sdpMid,
                sdpMLineIndex = ice.sdpMLineIndex
            };

            pc.AddIceCandidate(new RTCIceCandidate(candidateInit));
        }
    }

    private IEnumerator ConnectWebSocket()
    {
        bool connected = false;
        ws.OnOpen += () => { connected = true; };

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
            ws.Close();
        }
    }

    private RTCConfiguration GetConfiguration()
    {
        return new RTCConfiguration
        {
            iceServers = new RTCIceServer[] { }
        };
    }

    private IEnumerator SetupVideoDisplay()
    {
        int retryCount = 0;
        const int maxRetries = 20;

        while (retryCount < maxRetries)
        {
            if (videoStreamTrack != null)
            {
                var videoTexture = videoStreamTrack.Texture;
                if (videoTexture != null)
                {
                    if (videoMaterial != null)
                    {
                        videoMaterial.mainTexture = videoTexture;
                        videoMaterial.shader = Shader.Find("Unlit/Texture");
                    }

                    isVideoReceiving = true; // 修正: フィールドを使用
                    yield break;
                }
            }

            retryCount++;
            yield return new WaitForSeconds(0.5f);
        }

        Debug.LogError("Failed to setup video display after maximum retries.");
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 350, 150));
        GUILayout.Label($"WebSocket State: {(ws?.State.ToString() ?? "None")}");
        GUILayout.Label($"PeerConnection State: {(pc?.ConnectionState.ToString() ?? "None")}");
        GUILayout.Label($"Video Receiving: {isVideoReceiving}"); // 修正: isVideoReceivingを使用
        if (videoStreamTrack?.Texture != null)
        {
            GUILayout.Label($"Video Texture: {videoStreamTrack.Texture.width}x{videoStreamTrack.Texture.height}");
        }
        GUILayout.EndArea();
    }

    // ...existing code...
}