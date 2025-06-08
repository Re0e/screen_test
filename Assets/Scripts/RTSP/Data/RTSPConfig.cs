using UnityEngine;

namespace RTSP.Data
{
    [CreateAssetMenu(fileName = "RTSPConfig", menuName = "RTSP/Config")]
    public class RTSPConfig : ScriptableObject
    {
        [Header("Connection Settings")]
        public string websocketUrl = "ws://localhost:8080/ws";
        public float connectionTimeout = 5f;
        
        [Header("Video Settings")]
        public int videoWidth = 1920;
        public int videoHeight = 1080;
        public int maxRetries = 100;
        
        [Header("Debug Settings")]
        public bool enableDebugLogs = true;
        public bool showOnScreenDebug = true;
    }
}