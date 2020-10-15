using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.WebRTC;
using System.Text.RegularExpressions;
using Unity.RenderStreaming.Signaling;
using UnityEngine.InputSystem.EnhancedTouch;

namespace Unity.RenderStreaming
{
    using DataChannelDictionary = Dictionary<int, RTCDataChannel>;

    [Serializable]
    public class ButtonClickEvent : UnityEngine.Events.UnityEvent<int> { }

    [Serializable]
    public class ButtonClickElement
    {
        [Tooltip("Specifies the ID on the HTML")]
        public int elementId;
        public ButtonClickEvent click;
    }

    public class RenderStreaming : MonoBehaviour
    {

#pragma warning disable 0649

        [SerializeField, Tooltip("Rate control mode")]
        private string rateControlMode = "CBR";
        [SerializeField, Tooltip("Min bitrate in bps (6 - 50mbps) * 10 * 6")] [Range( 6000000, 50000000 )]
        private int minBitrate = 0;
        [SerializeField, Tooltip("Max bitrate in bps (6 - 50mbps) * 10 * 6")] [Range(6000000, 50000000)]
        private int maxBitrate = 50000000;
        [SerializeField, Tooltip("Width")] [Range(360, 3840)]
        private int width = 1280;
        [SerializeField, Tooltip("Height")] [Range(240, 2160)]
        private int height = 720;
        [SerializeField, Tooltip("VBR only: 0-51, lower values result in better quality but higher bitrate")] [Range(0,51)]
        private int minQP = 20;
        [SerializeField, Tooltip("VBR only: 0-51, lower values result in better quality but higher bitrate")] [Range(0,51)]
        private int maxQP = 20;
        [SerializeField, Tooltip("Minimal FPS for quality adaptation")] [Range(10, 30)]
        private int minFramerate = 30;
        [SerializeField, Tooltip("Maximum FPS for UnityRenderStreaming")] [Range(30,60)]
        private int maxFramerate = 30;
        [SerializeField, Tooltip("Suggest to deprecate in favour of width/height")] [Range(0,1)]
        private int intraRefreshPeriod = 30;
        [SerializeField, Tooltip("Error recovery: how often (in FPS) to refresh")] [Range(20,300)]
        private int intraRefreshCount = 10;
        [SerializeField, Tooltip("Error recovery: how much to refresh")] [Range(10,300)]
        private float scaleResolutionDownBy = 1.0f;


        [SerializeField, Tooltip("Signaling server url")]
        private string urlSignaling = "http://localhost"; 


        [SerializeField, Tooltip("Type of signaling server")]
        private string signalingType = typeof(HttpSignaling).FullName;

        [SerializeField, Tooltip("Array to set your own STUN/TURN servers")]
        private RTCIceServer[] iceServers = new RTCIceServer[]

        {
            new RTCIceServer()
            {
                urls = new string[] { "stun:stun.l.google.com:19302" }
            }
        };

        [SerializeField, Tooltip("Time interval for polling from signaling server")]
        private float interval = 5.0f;

        [SerializeField, Tooltip("Enable or disable hardware encoder")]
        private bool hardwareEncoderSupport = true;

        [SerializeField, Tooltip("Array to set your own click event")]
        private ButtonClickElement[] arrayButtonClickEvent;
#pragma warning restore 0649

        private ISignaling m_signaling;
        private readonly Dictionary<string, RTCPeerConnection> m_mapConnectionIdAndPeer = new Dictionary<string, RTCPeerConnection>();
        private readonly Dictionary<RTCPeerConnection, DataChannelDictionary> m_mapPeerAndChannelDictionary = new Dictionary<RTCPeerConnection, DataChannelDictionary>();
        private readonly Dictionary<RemoteInput, SimpleCameraController> m_remoteInputAndCameraController = new Dictionary<RemoteInput, SimpleCameraController>();
        private readonly Dictionary<RTCDataChannel, RemoteInput> m_mapChannelAndRemoteInput = new Dictionary<RTCDataChannel, RemoteInput>();
        private readonly List<SimpleCameraController> m_listController = new List<SimpleCameraController>();
        private readonly List<VideoStreamTrack> m_listVideoStreamTrack = new List<VideoStreamTrack>();
        private readonly Dictionary<MediaStreamTrack, List<RTCRtpSender>> m_mapTrackAndSenderList = new Dictionary<MediaStreamTrack, List<RTCRtpSender>>();
        private MediaStream m_audioStream;
        private DefaultInput m_defaultInput;
        private RTCConfiguration m_conf;

        public static RenderStreaming Instance { get; private set; }

        enum UnityEventType
        {
            SwitchVideo = 0
        }



        public void Awake()
        {
            Instance = this;
            var encoderType = hardwareEncoderSupport ? EncoderType.Hardware : EncoderType.Software;
            WebRTC.WebRTC.Initialize(encoderType);
            m_defaultInput = new DefaultInput();
            EnhancedTouchSupport.Enable();
        }

        public void OnDestroy()
        {
            Instance = null;
            EnhancedTouchSupport.Disable();
            WebRTC.WebRTC.Dispose();
            RemoteInputReceiver.Dispose();
            Unity.WebRTC.Audio.Stop();
        }
        public void Start()
        {
            m_audioStream = Unity.WebRTC.Audio.CaptureStream();

            m_conf = default;
            m_conf.iceServers = iceServers;
            StartCoroutine(WebRTC.WebRTC.Update());
        }

        void OnEnable()
        {
            if (this.m_signaling == null)
            {

                Type t = Type.GetType(signalingType);
                object[] args = { urlSignaling, interval };
                this.m_signaling = (ISignaling)Activator.CreateInstance(t, args);
                this.m_signaling.OnOffer += OnOffer;
                this.m_signaling.OnIceCandidate += OnIceCandidate;
            }
            this.m_signaling.Start();
        }

        public void SyncHardwareParameters()
        {
            foreach (var item in m_mapTrackAndSenderList)
            {
                foreach (var sender in item.Value)
                {
                    RTCRtpSendParameters parameters = sender.GetParameters();
                    foreach (var encoding in parameters.Encodings)
                    {
                        encoding.rateControlMode = rateControlMode;
                        encoding.minBitrate = Convert.ToUInt64(minBitrate);
                        encoding.maxBitrate = Convert.ToUInt64(maxBitrate);
                        encoding.width = Convert.ToUInt32(width);
                        encoding.height = Convert.ToUInt32(height);
                        encoding.minQP = Convert.ToUInt32(minQP);
                        encoding.maxQP = Convert.ToUInt32(maxP);
                        encoding.minFramerate = Convert.ToUInt32(minFramerate);
                        encoding.maxFramerate = Convert.ToUInt32(maxFramerate);

                        sender.SetHardwareParameters(parameters);
                    }
                }
            }
        }

        public void ChangeVideoParameters(VideoStreamTrack track, ulong? bitrate, uint? framerate)
        {
            foreach (var sender in m_mapTrackAndSenderList[track])
            {
                RTCRtpSendParameters parameters = sender.GetParameters();
                foreach (var encoding in parameters.Encodings)
                {
                    if (bitrate != null) encoding.maxBitrate = bitrate;
                    if (framerate != null) encoding.maxFramerate = framerate;
                }
                sender.SetParameters(parameters);
            }
        }

        public void AddController(SimpleCameraController controller)
        {
            m_listController.Add(controller);
            controller.SetInput(m_defaultInput);
        }

        public void RemoveController(SimpleCameraController controller)
        {
            m_listController.Remove(controller);
        }


        private bool isChanged = false;
        private int changeCount = 0;

        private void OnValidate()
        {
            isChanged = true;

        }

        private void Update()
        {
            if (isChanged)
            {
                changeCount += 1;
                if (changeCount > 100)
                {
                    //SyncAllParameters();
                    changeCount = 0;
                    isChanged = false;
                }
            }
        }

        public void AddVideoStreamTrack(VideoStreamTrack track)
        {
            m_listVideoStreamTrack.Add(track);
        }

        public void RemoveVideoStreamTrack(VideoStreamTrack track)
        {
            m_listVideoStreamTrack.Remove(track);
        }


        void OnDisable()
        {
            if (this.m_signaling != null)
            {
                this.m_signaling.Stop();
                this.m_signaling = null;
            }
        }

        void OnOffer(ISignaling signaling, DescData e)
        {
            RTCSessionDescription _desc;
            _desc.type = RTCSdpType.Offer;
            _desc.sdp = e.sdp;
            var connectionId = e.connectionId;
            if (m_mapConnectionIdAndPeer.ContainsKey(connectionId))
            {
                return;
            }
            var pc = new RTCPeerConnection();
            m_mapConnectionIdAndPeer.Add(e.connectionId, pc);

            pc.OnDataChannel = new DelegateOnDataChannel(channel => { OnDataChannel(pc, channel); });
            pc.SetConfiguration(ref m_conf);
            pc.OnIceCandidate = new DelegateOnIceCandidate(candidate =>
            {
                signaling.SendCandidate(e.connectionId, candidate);
            });
            pc.OnIceConnectionChange = new DelegateOnIceConnectionChange(state =>
            {
                if(state == RTCIceConnectionState.Disconnected)
                {
                    pc.Close();
                    m_mapConnectionIdAndPeer.Remove(e.connectionId);
                }
            });

            pc.SetRemoteDescription(ref _desc);
            foreach (var track in m_listVideoStreamTrack.Concat(m_audioStream.GetTracks()))
            {
                RTCRtpSender sender = pc.AddTrack(track);
                if (!m_mapTrackAndSenderList.TryGetValue(track, out List<RTCRtpSender> list))
                {
                    list = new List<RTCRtpSender>();
                    m_mapTrackAndSenderList.Add(track, list);
                }
                list.Add(sender);
            }

            RTCAnswerOptions options = default;
            var op = pc.CreateAnswer(ref options);
            while (op.MoveNext())
            {
            }
            if (op.IsError)
            {
                Debug.LogError($"Network Error: {op.Error}");
                return;
            }

            var desc = op.Desc;
            var opLocalDesc = pc.SetLocalDescription(ref desc);
            while (opLocalDesc.MoveNext())
            {
            }
            if (opLocalDesc.IsError)
            {
                Debug.LogError($"Network Error: {opLocalDesc.Error}");
                return;
            }

            signaling.SendAnswer(connectionId, desc);

            //SyncHardwareParameters();


        }

        void OnIceCandidate(ISignaling signaling, CandidateData e)
        {
            if (!m_mapConnectionIdAndPeer.TryGetValue(e.connectionId, out var pc))
            {
                return;
            }

            RTCIceCandidate​ _candidate = default;
            _candidate.candidate = e.candidate;
            _candidate.sdpMLineIndex = e.sdpMLineIndex;
            _candidate.sdpMid = e.sdpMid;

            pc.AddIceCandidate(ref _candidate);
        }

        void OnDataChannel(RTCPeerConnection pc, RTCDataChannel channel)
        {
            if (!m_mapPeerAndChannelDictionary.TryGetValue(pc, out var channels))
            {
                channels = new DataChannelDictionary();
                m_mapPeerAndChannelDictionary.Add(pc, channels);
            }
            channels.Add(channel.Id, channel);

            if (channel.Label != "data")
            {
                return;
            }

            RemoteInput input = RemoteInputReceiver.Create();
            input.ActionButtonClick = OnButtonClick;

            // device.current must be changed after creating devices
            m_defaultInput.MakeCurrent();

            m_mapChannelAndRemoteInput.Add(channel, input);
            channel.OnMessage = bytes => m_mapChannelAndRemoteInput[channel].ProcessInput(bytes);
            channel.OnClose = () => OnCloseChannel(channel);

            // find controller that not assigned remote input
            SimpleCameraController controller = m_listController
                .FirstOrDefault(_controller => !m_remoteInputAndCameraController.ContainsValue(_controller));

            if(controller != null)
            {
                controller.SetInput(input);
                m_remoteInputAndCameraController.Add(input, controller);

                byte index = (byte)m_listController.IndexOf(controller);
                byte[] bytes = {(byte)UnityEventType.SwitchVideo, index};
                channel.Send(bytes);
            }
        }

        void OnCloseChannel(RTCDataChannel channel)
        {
            RemoteInput input = m_mapChannelAndRemoteInput[channel];
            RemoteInputReceiver.Delete(input);

            // device.current must be changed after removing devices
            m_defaultInput.MakeCurrent();

            // reassign remote input to controller
            if (m_remoteInputAndCameraController.TryGetValue(input, out var controller))
            {
                RemoteInput newInput = FindPrioritizedInput();
                if (newInput == null)
                {
                    controller.SetInput(m_defaultInput);
                }
                else
                {
                    controller.SetInput(newInput);
                    m_remoteInputAndCameraController.Add(newInput, controller);
                }
            }
            m_remoteInputAndCameraController.Remove(input);

            m_mapChannelAndRemoteInput.Remove(channel);
        }

        RemoteInput FindPrioritizedInput()
        {
            var list = RemoteInputReceiver.All();

            // filter here
            // return null if not found the input
            return list.Except(m_remoteInputAndCameraController.Keys).FirstOrDefault();
        }

        void OnButtonClick(int elementId)
        {
            foreach (var element in arrayButtonClickEvent)
            {
                if (element.elementId == elementId)
                {
                    element.click.Invoke(elementId);
                }
            }
        }
    }
}
