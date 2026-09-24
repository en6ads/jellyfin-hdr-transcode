using System;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Moq;
using Xunit;

using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace Jellyfin.Controller.Tests.MediaEncoding;

public class EncodingHelperHdrPassthroughTests
{
    [Theory]
    [InlineData("hevc", "libx265", "smpte2084", "HDR10", "yuv420p10le")]
    [InlineData("av1", "libsvtav1", "smpte2084", "HDR10", "yuv420p10le")]
    [InlineData("av1", "libsvtav1", "arib-std-b67", "HLG", "yuv420p10le")]
    public void GetSwVidFilterChain_Passthrough_KeepsHdrAndTenBits(string codec, string encoder, string transfer, string range, string format)
    {
        var state = CreateState(codec, transfer, range);
        var options = CreateOptions(HardwareAccelerationType.none);
        var helper = CreateHelper();

        Assert.True(helper.IsHdrPassthroughAvailable(state, options));

        var (filters, _, _) = helper.GetSwVidFilterChain(state, options, encoder);
        var args = string.Join(',', filters);

        Assert.DoesNotContain("tonemapx", args, StringComparison.Ordinal);
        Assert.Contains("color_trc=" + transfer, args, StringComparison.Ordinal);
        Assert.DoesNotContain("color_trc=bt709", args, StringComparison.Ordinal);
        Assert.Contains("format=" + format, args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSwVidFilterChain_PassthroughDisabled_Tonemaps()
    {
        var state = CreateState("hevc", "smpte2084", "HDR10");
        var options = CreateOptions(HardwareAccelerationType.none);
        options.EnableHdrPassthrough = false;
        var helper = CreateHelper();

        var (filters, _, _) = helper.GetSwVidFilterChain(state, options, "libx265");

        Assert.Contains("tonemapx", string.Join(',', filters), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HardwareAccelerationType.none, false, true)]
    [InlineData(HardwareAccelerationType.qsv, false, true)]
    [InlineData(HardwareAccelerationType.qsv, true, true)]
    [InlineData(HardwareAccelerationType.nvenc, false, false)]
    [InlineData(HardwareAccelerationType.nvenc, true, false)]
    [InlineData(HardwareAccelerationType.vaapi, false, false)]
    [InlineData(HardwareAccelerationType.amf, false, false)]
    [InlineData(HardwareAccelerationType.videotoolbox, false, false)]
    public void IsHdrPassthroughAvailable_OnlyForVerifiedPipelines(HardwareAccelerationType accelerationType, bool hardwareEncoder, bool expected)
    {
        var state = CreateState("hevc", "smpte2084", "HDR10");
        var options = CreateOptions(accelerationType);
        options.EnableHardwareEncoding = hardwareEncoder;

        Assert.Equal(expected, CreateHelper().IsHdrPassthroughAvailable(state, options));
    }

    [Theory]
    [InlineData("SDR")]
    [InlineData("HLG")]
    [InlineData("SDR,DOVI")]
    public void IsHdrPassthroughAvailable_ClientCannotPresentOutputRange_False(string range)
    {
        var state = CreateState("hevc", "smpte2084", range);

        Assert.False(CreateHelper().IsHdrPassthroughAvailable(state, CreateOptions(HardwareAccelerationType.none)));
    }

    [Fact]
    public void IsHdrPassthroughAvailable_BurnedInSubtitles_False()
    {
        var state = CreateState("hevc", "smpte2084", "HDR10");
        state.SubtitleStream = new MediaStream { Type = MediaStreamType.Subtitle, Codec = "pgssub", Index = 2 };
        state.SubtitleDeliveryMethod = SubtitleDeliveryMethod.Encode;

        Assert.False(CreateHelper().IsHdrPassthroughAvailable(state, CreateOptions(HardwareAccelerationType.none)));
    }

    [Fact]
    public void IsHdrPassthroughAvailable_DoviProfile5_False()
    {
        var state = CreateState("hevc", "smpte2084", "HDR10");
        state.VideoStream.DvProfile = 5;
        state.VideoStream.DvBlSignalCompatibilityId = 0;
        state.VideoStream.RpuPresentFlag = 1;
        state.VideoStream.BlPresentFlag = 1;

        Assert.Equal(VideoRangeType.DOVI, state.VideoStream.VideoRangeType);
        Assert.False(CreateHelper().IsHdrPassthroughAvailable(state, CreateOptions(HardwareAccelerationType.none)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("main,main10", true)]
    [InlineData("Main 10", true)]
    [InlineData("main", false)]
    public void IsHdrPassthroughAvailable_Hevc_RequiresMain10(string? profiles, bool expected)
    {
        var state = CreateState("hevc", "smpte2084", "HDR10");
        state.BaseRequest.Profile = profiles;

        Assert.Equal(expected, CreateHelper().IsHdrPassthroughAvailable(state, CreateOptions(HardwareAccelerationType.none)));
    }

    [Fact]
    public void IsHdrPassthroughAvailable_H264Output_False()
    {
        var state = CreateState("h264", "smpte2084", "HDR10");

        Assert.False(CreateHelper().IsHdrPassthroughAvailable(state, CreateOptions(HardwareAccelerationType.none)));
    }

    private static EncodingOptions CreateOptions(HardwareAccelerationType accelerationType)
        => new()
        {
            EnableHdrPassthrough = true,
            HardwareAccelerationType = accelerationType,
            EnableHardwareEncoding = true,
            EnableTonemapping = true
        };

    private static EncodingJobInfo CreateState(string outputCodec, string transfer, string requestedRanges)
    {
        var stream = new MediaStream
        {
            Type = MediaStreamType.Video,
            Codec = "hevc",
            Width = 3840,
            Height = 2160,
            BitDepth = 10,
            PixelFormat = "yuv420p10le",
            ColorSpace = "bt2020nc",
            ColorPrimaries = "bt2020",
            ColorTransfer = transfer
        };

        return new EncodingJobInfo(TranscodingJobType.Hls)
        {
            VideoStream = stream,
            MediaSource = new MediaSourceInfo { Container = "mkv", MediaStreams = [stream] },
            BaseRequest = new VideoRequestDto { VideoRangeType = requestedRanges },
            OutputVideoCodec = outputCodec,
            VideoType = VideoType.VideoFile,
            IsVideoRequest = true,
            IsInputVideo = true
        };
    }

    private static EncodingHelper CreateHelper()
    {
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(x => x.SupportsFilter(It.IsAny<string>())).Returns(true);
        encoder.Setup(x => x.SupportsEncoder(It.IsAny<string>())).Returns(true);
        encoder.SetupGet(x => x.EncoderVersion).Returns(new Version(8, 1));

        return new EncodingHelper(
            Mock.Of<IApplicationPaths>(),
            encoder.Object,
            Mock.Of<ISubtitleEncoder>(),
            Mock.Of<IConfiguration>(),
            Mock.Of<IConfigurationManager>(),
            Mock.Of<IPathManager>());
    }
}
