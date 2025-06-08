using System;

namespace RTSP.Data
{
    [Serializable]
    public class IceCandidateMessage
    {
        public string candidate;
        public string sdpMid;
        public int sdpMLineIndex;
    }
}