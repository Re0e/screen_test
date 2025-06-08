using System.Collections;
using Unity.WebRTC;
using UnityEngine;
using RTSP.Data;

namespace RTSP
{
    public class VideoDisplayManager : MonoBehaviour
    {
        [SerializeField] private GameObject videoPlane;
        
        private VideoStreamTrack videoStreamTrack;
        private Material videoMaterial;
        private RenderTexture videoRenderTexture;
        private bool isVideoReceiving = false;
        private RTSPConfig config;
        
        public bool IsReceiving => isVideoReceiving;
        public VideoStreamTrack VideoTrack => videoStreamTrack;
        
        public void Initialize(RTSPConfig config)
        {
            this.config = config;
            CreateVideoRenderTexture();
            SetupVideoPlane();
        }
        
        private void CreateVideoRenderTexture()
        {
            videoRenderTexture = new RenderTexture(config.videoWidth, config.videoHeight, 0, RenderTextureFormat.BGRA32);
            videoRenderTexture.name = "VideoStreamTexture";
            videoRenderTexture.Create();
        }
        
        private void SetupVideoPlane()
        {
            if (videoPlane != null)
            {
                var planeRenderer = videoPlane.GetComponent<Renderer>();
                if (planeRenderer != null)
                {
                    videoMaterial = new Material(Shader.Find("Unlit/Texture"));
                    planeRenderer.material = videoMaterial;
                }
            }
        }
        
        public void SetVideoTrack(VideoStreamTrack track)
        {
            videoStreamTrack = track;
            StartCoroutine(SetupVideoDisplay());
        }
        
        private IEnumerator SetupVideoDisplay()
        {
            int retryCount = 0;
            
            while (retryCount < config.maxRetries && !isVideoReceiving)
            {
                retryCount++;
                
                if (videoStreamTrack != null)
                {
                    var videoTexture = videoStreamTrack.Texture;
                    
                    if (videoTexture != null)
                    {
                        if (videoMaterial != null)
                        {
                            videoMaterial.mainTexture = videoTexture;
                            videoMaterial.shader = Shader.Find("Unlit/Texture");
                            isVideoReceiving = true;
                            Debug.Log("Video display setup completed successfully!");
                            yield break;
                        }
                    }
                }
                
                yield return new WaitForSeconds(0.1f);
            }
        }
        
        public void Update()
        {
            if (videoStreamTrack != null && !isVideoReceiving)
            {
                var texture = videoStreamTrack.Texture;
                if (texture != null && videoMaterial != null)
                {
                    videoMaterial.mainTexture = texture;
                    videoMaterial.shader = Shader.Find("Unlit/Texture");
                    isVideoReceiving = true;
                }
            }
        }
    }
}