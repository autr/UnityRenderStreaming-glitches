# Unity HDR WebRTC Example

Example project for testing HDR render streaming and fork with exposed NVIDIA API controls. To clone repo use:

```
git clone --recursive https://github.com/autr/UnityRenderStreaming-glitches
git submodule init && git submodule update
```

Links:

* [Plugin](https://github.com/autr/com.unity.webrtc) (`autr/com.unity.webrtc:2.1.3-nvidia`)
* [Notes](https://github.com/autr/unity-notes) (`autr/unity-notes`)

## Overview

`RenderStreaming.cs` script exposes GUI parameters sync'd to a singleton object inside `HWSettings` C++ plugin. These settings are used during initialisation of the `NvEncoder.cpp` encoder, so the scene must be stopped/started for new changes to take effect.

`RenderStreamDebugger.cs` logs out the bitrate and framerate called in `NvEncoder::UpdateSettings` requested by the `libwebrtc` connection in `DummyVideoEncoder.cpp`. These are useful to see how the encoder and webrtc connection respond to different settings (see also notes of FPS below).


## Controls

Explanation of controls available in `RenderStreaming.cs`:

### Rate Control Mode

**only set if: `always set`**

Constant is the recommended default for low-latency streaming. 

`NV_ENC_CONFIG_H264::rateControlMode`

### Min Bitrate

**only set if: `> 0`**

Variously used by all rate control modes, as **average** and **target** bitrate depending on which one.

`NV_ENC_RC_PARAMS::averageBitRate`

### Max Bitrate

**only set if: `> 0`**

Only used by Variable rate control mode.

`NV_ENC_RC_PARAMS::maxBitrate`

### Width

*Not implemented*

### Height

*Not implemented*

### Min Framerate

*Not implemented*

See notes.

### Max Framerate

*Not implemented*

See notes.

### Min QP

**only set if: `> 0`**

Used by Constant rate control modes, and in place of `minBitrate` for VBR modes.

`NV_ENC_RC_PARAMS::minQP`

### Max QP

**only set if: `> 0`**

Used by VBR only.

`NV_ENC_RC_PARAMS::maxQP`

### Intra Refresh Period + Intra Refresh Count

**only set if: `intraRefreshPeriod > 0 && intraRefreshCount > 0`**

An error resilience mechanism (`enableIntraRefresh`, `intraRefreshPeriod`, `intraRefreshCount`):

```
NVENCODE API provides a mechanism to implement intra refresh. The enableIntraRefresh flag should be set to 1 in order to enable intra refresh. intraRefreshPeriod determines the period after which intra refresh would happen again and intraRefreshCnt sets the number of frames over which intra refresh would happen.

Intra Refresh causes consecutive sections of the frames to be encoded using intra macroblocks, over intraRefreshCnt consecutive frames. Then the whole cycle repeats after intraRefreshPeriod frames from the first intra-refresh frame. It is essential to set intraRefreshPeriod and intraRefreshCnt appropriately based on the probability of errors that may occur during transmission. For example, intraRefreshPeriod may be small like 30 for a highly error prone network thus enabling recovery every second for a 30 FPS video stream. For networks that have lesser chances of error, the value may be set higher. Lower value of intraRefreshPeriod comes with a slightly lower quality as a larger portion of the overall macroblocks in an intra refresh period are forced to be intra coded, but provides faster recovery from network errors.

intraRefreshCnt determines the number of frames over which the intra refresh will happen within an intra refresh period. A smaller value of intraRefreshCnt will refresh the entire frame quickly (instead of refreshing it slowly in bands) and hence enable a faster error recovery. However, a lower intraRefreshCnt also means sending a larger number of intra macroblocks per frame and hence slightly lower quality.
```

Recommended settings to try:

```
Period = 60
Count = 40

Period = 30
Count = 20

etc...
```

### Enable AQ

**only set if: `true`**

Adaptive quantisation `enableAQ`; possibly recommended for low latency streaming.

`NV_ENC_RC_PARAMS::enableAQ`

### Max Num Ref Frames

**only set if: `> 0`**

Encoder will use codec default if not set: higher values possibly recommended for error resiliency (ie. "use more reference frames to avoid errors" `maxNumRefFrames`). 

`NV_ENC_CONFIG_H264::enableAQ`

### Infinite GOP

**only set if: `true`**

Changes `gopLength` from framerate to `NVENC_INFINITE_GOPLENGTH` - a recommended setting for low latency streaming.

`NV_ENC_CONFIG::gopLength`

## Notes

Framerates

Observing the incoming FPS from WebRTC in `RenderStreamDebugger.cs` and we average 60 FPS. Setting FPS instead via the WebRTC initialisation (`webrtc::VideoCodec::maxFramerate`), or via NVIDIA encoder initialisation or update (`NV_ENC_INITIALIZE_PARAMS::frameRateNum`) and more glitches / unusual behaviour are produced. Needs more investigation.

**pliCount, firCount, nackCount**

Inside `chrome://webrtc-internals` only `pliCount` is being sent, and no `firCount` or `nackCount`. Possibly needs more investigation.

## Experiments

Official recommendations:

```
Low-latency use cases like game-streaming, video conferencing etc.

Ultra-low latency or low latency Tuning Info
Rate control mode = CBR
Multi Pass – Quarter/Full (evaluate and decide)
Very low VBV buffer size (e.g. single frame = bitrate/framerate)
No B Frames
Infinite GOP length
Adaptive quantization (AQ) enabled**
Long term reference pictures***
Intra refresh***
Non-reference P frames***
Force IDR***

*: Recommended for low motion games and natural video.
**: Recommended on second generation Maxwell GPUs and above.
***: These features are useful for error recovery during transmission across noisy mediums.
```