using System;
using System.Collections;
using Unity.WebRTC;
using UnityEngine;
using RTSP.Data;

namespace RTSP
{
    public class WebRTCManager : MonoBehaviour
    {
        public event Action<VideoStreamTrack> OnVideoTrackReceived;
        public event Action<RTCPeerConnectionState> OnConnectionStateChanged;
        public event Action<RTCIceConnectionState> OnIceConnectionStateChanged;
        public event Action<RTCIceCandidate> OnIceCandidate;
        
        private RTCPeerConnection pc;
        private RTSPConfig config;
        
        public RTCPeerConnectionState? ConnectionState => pc?.ConnectionState;
        
        public void Initialize(RTSPConfig config)
        {
            this.config = config;
        }
        
        public IEnumerator SetupPeerConnection()
        {
            var rtcConfig = new RTCConfiguration
            {
                iceServers = new RTCIceServer[] { }
            };
            
            pc = new RTCPeerConnection(ref rtcConfig);
            
            var transceiverInit = new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly };
            pc.AddTransceiver(TrackKind.Video, transceiverInit);
            
            pc.OnIceCandidate = candidate => OnIceCandidate?.Invoke(candidate);
            pc.OnConnectionStateChange = state => OnConnectionStateChanged?.Invoke(state);
            pc.OnIceConnectionChange = state => OnIceConnectionStateChanged?.Invoke(state);
            pc.OnTrack = OnTrackReceived;
            
            yield return null;
        }
        
        private void OnTrackReceived(RTCTrackEvent trackEvent)
        {
            if (trackEvent.Track.Kind == TrackKind.Video)
            {
                var videoTrack = trackEvent.Track as VideoStreamTrack;
                if (videoTrack != null)
                {
                    OnVideoTrackReceived?.Invoke(videoTrack);
                }
            }
        }
        
        public IEnumerator CreateOffer()
        {
            var offerOp = pc.CreateOffer();
            yield return offerOp;
            
            if (!offerOp.IsError)
            {
                var desc = offerOp.Desc;
                var localOp = pc.SetLocalDescription(ref desc);
                yield return localOp;
                
                if (!localOp.IsError)
                {
                    yield return new SdpMessage(desc);
                }
            }
        }
        
        public IEnumerator SetRemoteDescription(SdpMessage sdpMessage)
        {
            RTCSessionDescription desc = new RTCSessionDescription
            {
                type = Enum.TryParse(sdpMessage.type, true, out RTCSdpType type) ? type : RTCSdpType.Offer,
                sdp = sdpMessage.sdp
            };
            
            var remoteOp = pc.SetRemoteDescription(ref desc);
            yield return remoteOp;
            
            if (remoteOp.IsError)
            {
                Debug.LogError("SetRemoteDescription failed: " + remoteOp.Error.message);
            }
        }
        
        public void AddIceCandidate(IceCandidateMessage iceMessage)
        {
            RTCIceCandidateInit candidateInit = new RTCIceCandidateInit
            {
                candidate = iceMessage.candidate,
                sdpMid = iceMessage.sdpMid,
                sdpMLineIndex = iceMessage.sdpMLineIndex
            };
            
            pc.AddIceCandidate(new RTCIceCandidate(candidateInit));
        }
        
        public void Dispose()
        {
            pc?.Dispose();
        }
    }
}