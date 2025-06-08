using System;
using System.Linq;
using Unity.WebRTC;
using UnityEngine;

namespace Utils
{
    public static class CodecChecker
    {
        public static bool CheckH264Support()
        {
            try
            {
                var capabilities = RTCRtpSender.GetCapabilities(TrackKind.Video);
                if (capabilities?.codecs != null)
                {
                    return capabilities.codecs.Any(codec => 
                        codec.mimeType.Contains("H264", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to check codec capabilities: " + ex.Message);
            }
            return false;
        }
        
        public static void LogAvailableCodecs()
        {
            try
            {
                var capabilities = RTCRtpSender.GetCapabilities(TrackKind.Video);
                if (capabilities?.codecs != null)
                {
                    Debug.Log($"Available video codecs count: {capabilities.codecs.Length}");
                    foreach (var codec in capabilities.codecs)
                    {
                        Debug.Log($"Available codec: {codec.mimeType}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to log codecs: " + ex.Message);
            }
        }
    }
}