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
    private RenderTexture videoRenderTexture;
    private void Start()
    {
        Debug.Log("Start RTSP Video Receiver Setup with H.264 support");
        
        // 事前にRenderTextureを作成
        CreateVideoRenderTexture();
        
        CheckH264Support();
        SetupVideoPlane();
        StartCoroutine(SetupConnection());
    }

    private void CreateVideoRenderTexture()
    {
        // H.264用のRenderTextureを作成
        videoRenderTexture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.BGRA32);
        videoRenderTexture.name = "VideoStreamTexture";
        videoRenderTexture.Create();
        Debug.Log($"Created RenderTexture: {videoRenderTexture.width}x{videoRenderTexture.height}");
    }

    void Update()
    {
        ws?.DispatchMessageQueue();

        // より頻繁にテクスチャをチェック
        if (videoStreamTrack != null && !isVideoReceiving)
        {
            var texture = videoStreamTrack.Texture;
            if (texture != null && videoMaterial != null)
            {
                Debug.Log($"Late texture detection in Update! Size: {texture.width}x{texture.height}");
                videoMaterial.mainTexture = texture;
                videoMaterial.shader = Shader.Find("Unlit/Texture");
                isVideoReceiving = true;
            }
        }

        // デバッグ情報を定期的に出力
        if (Time.frameCount % 300 == 0 && videoStreamTrack != null && !isVideoReceiving)
        {
            Debug.Log($"Debug: VideoStreamTrack.Enabled={videoStreamTrack.Enabled}, ReadyState={videoStreamTrack.ReadyState}, Texture={videoStreamTrack.Texture}");
        }
        
        if (isVideoReceiving && Time.frameCount % 300 == 0)
        {
            var texture = videoStreamTrack?.Texture;
            Debug.Log($"Video Status - Receiving: {isVideoReceiving}");
            Debug.Log($"Material Texture: {videoMaterial?.mainTexture}");
            Debug.Log($"VideoStreamTrack Texture: {texture}");
            Debug.Log($"RenderTexture: {videoRenderTexture} (Created: {videoRenderTexture?.IsCreated()})");
            
            if (texture != null)
            {
                Debug.Log($"StreamTrack Texture Size: {texture.width}x{texture.height}");
            }
            if (videoRenderTexture != null)
            {
                Debug.Log($"RenderTexture Size: {videoRenderTexture.width}x{videoRenderTexture.height}");
            }
        }
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
        if (videoPlane != null)
        {
            var planeRenderer = videoPlane.GetComponent<Renderer>(); // 修正: 変数名を planeRenderer に変更
            if (planeRenderer != null)
            {
                videoMaterial = new Material(Shader.Find("Unlit/Texture"));
                planeRenderer.material = videoMaterial;
                Debug.Log("Video material created and assigned to plane");

                // Planeの詳細を確認
                Debug.Log($"Plane position: {videoPlane.transform.position}");
                Debug.Log($"Plane rotation: {videoPlane.transform.rotation}");
                Debug.Log($"Plane scale: {videoPlane.transform.localScale}");
                Debug.Log($"Renderer enabled: {planeRenderer.enabled}");
                Debug.Log($"GameObject active: {videoPlane.activeInHierarchy}");
            }
            else
            {
                Debug.LogError("Renderer not found on video plane!");
            }
        }
        else
        {
            Debug.LogError("Video plane is not assigned!");
            return;
        }
    }
    
    private IEnumerator WaitAndSetupVideoDisplay()
    {
        // 短時間待ってからセットアップを開始
        yield return new WaitForSeconds(2.0f); // 修正: 待機時間を1秒から2秒に増加
        yield return StartCoroutine(SetupVideoDisplay());
    }
    
    private IEnumerator MonitorVideoTexture()
    {
        Debug.Log("Starting video texture monitoring...");
        
        int frameCount = 0;
        while (!isVideoReceiving && videoStreamTrack != null && frameCount < 1000)
        {
            frameCount++;
            
            // VideoStreamTrackのTextureをチェック
            var texture = videoStreamTrack.Texture;
            if (texture != null)
            {
                Debug.Log($"New texture detected! Size: {texture.width}x{texture.height}");
                
                if (videoMaterial != null)
                {
                    videoMaterial.mainTexture = texture; // 修正: RenderTextureの割り当てを削除
                    videoMaterial.shader = Shader.Find("Unlit/Texture");
                    isVideoReceiving = true;
                    Debug.Log("Video texture applied successfully in monitor!");
                }
                yield break;
            }
            
            // RenderTextureの状態をチェック
            if (videoRenderTexture != null && videoRenderTexture.IsCreated())
            {
                if (frameCount % 100 == 0)
                {
                    Debug.Log($"RenderTexture status: Created={videoRenderTexture.IsCreated()}, Active={RenderTexture.active == videoRenderTexture}");
                }
            }
            
            yield return new WaitForSeconds(0.1f);
        }
        
        Debug.Log($"Video texture monitoring ended after {frameCount} frames");
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
            Debug.Log($"Track received - Kind: {trackEvent.Track.Kind}, ID: {trackEvent.Track.Id}");
            
            if (trackEvent.Track.Kind == TrackKind.Video)
            {
                videoStreamTrack = trackEvent.Track as VideoStreamTrack;
                Debug.Log("Video track received! Setting up video display...");
                
                if (videoStreamTrack != null)
                {
                    Debug.Log($"VideoStreamTrack details - Enabled: {videoStreamTrack.Enabled}, ReadyState: {videoStreamTrack.ReadyState}");
                    
                    // RenderTextureを直接適用せず、VideoStreamTrackのTextureを利用
                    if (videoMaterial != null && videoRenderTexture != null)
                    {
                        videoMaterial.mainTexture = videoRenderTexture; // 修正: RenderTextureを直接適用
                        videoMaterial.shader = Shader.Find("Unlit/Texture");
                        isVideoReceiving = true;
                        Debug.Log("Video texture applied to material immediately!");
                    }
                    
                    // モニタリングも継続
                    StartCoroutine(MonitorVideoTexture());
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
        const int maxRetries = 100; // リトライ回数をさらに増やす
        
        Debug.Log($"Starting SetupVideoDisplay - VideoStreamTrack: {videoStreamTrack != null}");

        while (retryCount < maxRetries && !isVideoReceiving)
        {
            retryCount++;
            
            if (retryCount % 10 == 1) // 10回に1回ログ出力
            {
                Debug.Log($"Setting up video display on Plane (attempt {retryCount})");
            }
            
            if (videoStreamTrack != null)
            {
                var videoTexture = videoStreamTrack.Texture;
                
                if (videoTexture != null)
                {
                    Debug.Log($"Video texture found! Size: {videoTexture.width}x{videoTexture.height}"); // 修正: formatを削除
                    
                    if (videoMaterial != null)
                    {
                        videoMaterial.mainTexture = videoTexture;
                        videoMaterial.shader = Shader.Find("Unlit/Texture");
                        Debug.Log("Video texture applied to material successfully!");
                        
                        isVideoReceiving = true;
                        Debug.Log("Video display setup completed successfully!");
                        yield break;
                    }
                }
            }

            yield return new WaitForSeconds(0.1f);
        }

        if (!isVideoReceiving)
        {
            Debug.LogError("Failed to setup video display after maximum retries");
        }
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