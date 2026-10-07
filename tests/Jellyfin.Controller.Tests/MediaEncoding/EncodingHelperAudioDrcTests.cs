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

public class EncodingHelperAudioDrcTests
{
    [Theory]
    [InlineData("ac3")]
    [InlineData("eac3")]
    [InlineData("EAC3")]
    public void GetInputModifier_Ac3Family_DisabledDrc_PassesDrcScaleZero(string codec)
    {
        var state = CreateState(codec, "aac");

        var modifier = CreateHelper().GetInputModifier(state, new EncodingOptions { DisableAc3DynamicRangeControl = true }, null);

        Assert.Contains("-drc_scale 0", modifier, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ac3")]
    [InlineData("eac3")]
    public void GetInputModifier_OptionOff_NoDrcScale(string codec)
    {
        var state = CreateState(codec, "aac");

        var modifier = CreateHelper().GetInputModifier(state, new EncodingOptions(), null);

        Assert.DoesNotContain("-drc_scale", modifier, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dts")]
    [InlineData("truehd")]
    [InlineData("aac")]
    public void GetInputModifier_OtherCodecs_NoDrcScale(string codec)
    {
        var state = CreateState(codec, "aac");

        var modifier = CreateHelper().GetInputModifier(state, new EncodingOptions { DisableAc3DynamicRangeControl = true }, null);

        Assert.DoesNotContain("-drc_scale", modifier, StringComparison.Ordinal);
    }

    [Fact]
    public void GetInputModifier_AudioCopied_NoDrcScale()
    {
        var state = CreateState("eac3", "copy");

        var modifier = CreateHelper().GetInputModifier(state, new EncodingOptions { DisableAc3DynamicRangeControl = true }, null);

        Assert.DoesNotContain("-drc_scale", modifier, StringComparison.Ordinal);
    }

    [Fact]
    public void GetInputModifier_NoAudioStream_NoDrcScale()
    {
        var state = CreateState("eac3", "aac");
        state.AudioStream = null;

        var modifier = CreateHelper().GetInputModifier(state, new EncodingOptions { DisableAc3DynamicRangeControl = true }, null);

        Assert.DoesNotContain("-drc_scale", modifier, StringComparison.Ordinal);
    }

    private static EncodingJobInfo CreateState(string inputAudioCodec, string outputAudioCodec)
    {
        var audio = new MediaStream { Type = MediaStreamType.Audio, Codec = inputAudioCodec, Channels = 6, Index = 1 };

        return new EncodingJobInfo(TranscodingJobType.Hls)
        {
            AudioStream = audio,
            MediaSource = new MediaSourceInfo { Container = "mkv", MediaStreams = [audio] },
            BaseRequest = new VideoRequestDto(),
            OutputAudioCodec = outputAudioCodec,
            VideoType = VideoType.VideoFile
        };
    }

    private static EncodingHelper CreateHelper()
    {
        var encoder = new Mock<IMediaEncoder>();
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
