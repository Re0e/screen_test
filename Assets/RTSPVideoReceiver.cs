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
        Debug.Log("Checking H.264 support...");
        try
        {
            var capabilities = RTCRtpSender.GetCapabilities(TrackKind.Video);
            if (capabilities?.codecs != null)
            {
                bool h264Found = capabilities.codecs.Any(codec => codec.mimeType.Contains("H264", StringComparison.OrdinalIgnoreCase)); // 修正: System.LinqのAnyを使用
                Debug.Log(h264Found ? "SUCCESS: H.264 codec is available!" : "H.264 codec not found - will use VP8/VP9");
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
        var config = GetConfiguration();
        pc = new RTCPeerConnection(ref config);
        Debug.Log("PeerConnection created");

        // H.264受信用のトランシーバーを作成（ダミートラック不要）
        Debug.Log("Adding video transceiver...");
        var transceiverInit = new RTCRtpTransceiverInit
        {
            direction = RTCRtpTransceiverDirection.RecvOnly
        };
        
        var transceiver = pc.AddTransceiver(TrackKind.Video, transceiverInit);
        Debug.Log("Video transceiver added");

        pc.OnIceCandidate = candidate =>
        {
            if (candidate == null)
            {
                Debug.Log("ICE gathering completed (null candidate received)");
                return;
            }

            Debug.Log($"Unity ICE candidate: {candidate.Candidate}");

            IceCandidateMessage msg = new IceCandidateMessage()
            {
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex ?? 0
            };

            string candidateJson = JsonUtility.ToJson(msg);
            Debug.Log("Sending ICE candidate JSON: " + candidateJson);
            ws.SendText("ice:" + candidateJson);
        };

        pc.OnIceConnectionChange = state =>
        {
            Debug.Log("ICE connection state changed: " + state);
        };

        pc.OnConnectionStateChange = state =>
        {
            Debug.Log("Peer Connection State: " + state);
        };

        pc.OnTrack = (RTCTrackEvent trackEvent) =>
        {
            Debug.Log($"Track received - Kind: {trackEvent.Track.Kind}, ID: {trackEvent.Track.Id}");
            
            if (trackEvent.Track.Kind == TrackKind.Video)
            {
                Debug.Log("Video track received! Setting up video display...");
                
                videoStreamTrack = trackEvent.Track as VideoStreamTrack;
                if (videoStreamTrack != null)
                {
                    StartCoroutine(SetupVideoDisplay());
                }
            }
        };

        Debug.Log("Creating offer...");
        var offerOp = pc.CreateOffer();
        yield return offerOp;
        
        if (offerOp.IsError)
        {
            Debug.LogError("Failed to create offer: " + offerOp.Error.message);
            yield break;
        }
        
        Debug.Log("Offer created successfully");
        var desc = offerOp.Desc;
        
        Debug.Log("Generated SDP Offer content:\n" + desc.sdp);
        if (desc.sdp.Contains("H264") || desc.sdp.Contains("h264") || desc.sdp.Contains("video/H264"))
        {
            Debug.Log("SUCCESS: H.264 codec found in Unity SDP offer");
        }
        else
        {
            Debug.LogWarning("H.264 not found in SDP, using VP8/VP9 codecs");
        }

        Debug.Log("Setting local description...");
        var localOp = pc.SetLocalDescription(ref desc);
        yield return localOp;
        
        if (localOp.IsError)
        {
            Debug.LogError("Failed to set local description: " + localOp.Error.message);
            yield break;
        }
        
        Debug.Log("Local description set successfully");

        SdpMessage sdpMsg = new SdpMessage(desc);
        string sdpJson = JsonUtility.ToJson(sdpMsg);
        Debug.Log("Sending SDP offer: " + sdpJson);
        ws.SendText("sdp:" + sdpJson);
    }

    private RTCConfiguration GetConfiguration()
    {
        RTCConfiguration config = default;
        config.iceServers = new RTCIceServer[] {};
        
        return config;
    }

    private IEnumerator SetupVideoDisplay()
    {
        int retryCount = 0;
        const int maxRetries = 20; // 最大10秒間リトライ
        
        while (retryCount < maxRetries)
        {
            if (videoStreamTrack != null)
            {
                Debug.Log($"Setting up video display on Plane (attempt {retryCount + 1})");
                
                var videoTexture = videoStreamTrack.Texture;
                if (videoTexture != null)
                {
                    Debug.Log($"Video texture obtained: {videoTexture.width}x{videoTexture.height}");
                    
                    if (videoMaterial != null)
                    {
                        videoMaterial.mainTexture = videoTexture;
                        videoMaterial.shader = Shader.Find("Unlit/Texture");
                        Debug.Log("Video texture applied to plane material");
                    }
                    
                    isVideoReceiving = true;
                    Debug.Log("Video stream display on Plane completed!");
                    yield break; // 成功時は終了
                }
            }
            
            retryCount++;
            Debug.LogWarning($"VideoStreamTrack.Texture is null, retrying... ({retryCount}/{maxRetries})");
            yield return new WaitForSeconds(0.5f);
        }
        
        Debug.LogError("Failed to setup video display after maximum retries");
    }

    private IEnumerator OnSignalingMessage(string message)
    {
        if (message.StartsWith("sdp:"))
        {
            string sdpJson = message.Substring(4);
            Debug.Log("Received SDP message: " + sdpJson);

            var sdpMsg = JsonUtility.FromJson<SdpMessage>(sdpJson);
            
            RTCSessionDescription desc = new RTCSessionDescription();
            
            if (sdpMsg.type.ToLower() == "offer")
                desc.type = RTCSdpType.Offer;
            else if (sdpMsg.type.ToLower() == "answer")
                desc.type = RTCSdpType.Answer;
            else if (sdpMsg.type.ToLower() == "pranswer")
                desc.type = RTCSdpType.Pranswer;
            else if (sdpMsg.type.ToLower() == "rollback")
                desc.type = RTCSdpType.Rollback;
                
            desc.sdp = sdpMsg.sdp;

            Debug.Log("Received SDP content:\n" + desc.sdp);
            if (desc.sdp.Contains("H264") || desc.sdp.Contains("h264") || desc.sdp.Contains("102"))
            {
                Debug.Log("SUCCESS: H.264 codec confirmed in received SDP from Go");
            }
            else if (desc.sdp.Contains("VP8") || desc.sdp.Contains("vp8") || desc.sdp.Contains("127"))
            {
                Debug.Log("VP8 codec confirmed in received SDP from Go");
            }

            Debug.Log($"Setting remote description (type: {desc.type})...");
            var remoteOp = pc.SetRemoteDescription(ref desc);
            yield return remoteOp;
            
            if (remoteOp.IsError)
            {
                Debug.LogError("SetRemoteDescription failed: " + remoteOp.Error.message);
            }
            else
            {
                Debug.Log("Remote description set successfully");
            }
        }
        else if (message.StartsWith("ice:"))
        {
            string iceJson = message.Substring(4);
            Debug.Log("Received ICE candidate: " + iceJson);

            var ice = JsonUtility.FromJson<IceCandidateMessage>(iceJson);
            
            RTCIceCandidateInit candidateInit = new RTCIceCandidateInit
            {
                candidate = ice.candidate,
                sdpMid = ice.sdpMid,
                sdpMLineIndex = ice.sdpMLineIndex
            };
            
            try
            {
                pc.AddIceCandidate(new RTCIceCandidate(candidateInit));
                Debug.Log("Added ICE candidate successfully");
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
        ws.OnError += (e) => { Debug.LogError("WebSocket connection error: " + e); };

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
            ws.Close();
        }
        else
        {
            Debug.Log("WebSocket connected.");
        }
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        ws?.DispatchMessageQueue();
#endif
    }

    void OnDestroy()
    {
        Debug.Log("Cleaning up video WebRTC and WebSocket");
        
        if (videoMaterial != null)
        {
            Destroy(videoMaterial);
        }
        
        videoStreamTrack?.Dispose();
        pc?.Close();

        if (ws != null)
        {
            ws.OnOpen -= () => { }; // 修正: イベントリスナー解除
            ws.OnError -= (e) => { };
            ws.OnClose -= (e) => { };
            ws.OnMessage -= (bytes) => { };
            ws.Close();
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 350, 150));
        GUILayout.Label($"WebSocket State: {(ws?.State.ToString() ?? "None")}");
        GUILayout.Label($"PeerConnection State: {(pc?.ConnectionState.ToString() ?? "None")}");
        GUILayout.Label($"Video Receiving: {isVideoReceiving}");
        if (videoStreamTrack?.Texture != null)
        {
            GUILayout.Label($"Video Texture: {videoStreamTrack.Texture.width}x{videoStreamTrack.Texture.height}");
        }
        GUILayout.EndArea();
    }
}