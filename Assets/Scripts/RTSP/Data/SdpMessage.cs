using System;
using Unity.WebRTC;

namespace RTSP.Data
{
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
}