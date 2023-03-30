using System;
using System.Collections.Generic;
using System.Linq;
using Vortice.MediaFoundation;

namespace FlyleafLib.VideoDeviceEnumerator;

public static class VideoFormatsViaMediaSource
{
    private static readonly Guid MfMediaTypeVideo =
        new(0x73646976, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);

    public static IEnumerable<IVideoFormat> GetVideoFormatsForVideoDevice(string friendlyName,
        string symbolicLink)
    {
        var formatList = new List<IVideoFormat>();

        using (var mediaSource = GetMediaSourceFromVideoDevice(symbolicLink))
        {
            using (var sourcePresentationDescriptor = mediaSource.CreatePresentationDescriptor())
            {
                var sourceStreamCount = sourcePresentationDescriptor.StreamDescriptorCount;

                for (var i = 0; i < sourceStreamCount; i++)
                {
                    var guidMajorType =
                        GetMajorMediaTypeFromPresentationDescriptor(sourcePresentationDescriptor, i);
                    if (guidMajorType != MfMediaTypeVideo) continue;

                    sourcePresentationDescriptor.GetStreamDescriptorByIndex(i, out var streamIsSelected,
                        out var videoStreamDescriptor);

                    using (videoStreamDescriptor)
                    {
                        if (streamIsSelected == false) continue;

                        using (var typeHandler = videoStreamDescriptor.MediaTypeHandler)
                        {
                            var mediaTypeCount = typeHandler.MediaTypeCount;

                            for (var mediaTypeId = 0; mediaTypeId < mediaTypeCount; mediaTypeId++)
                                using (var workingMediaType = typeHandler.GetMediaTypeByIndex(mediaTypeId))
                                {
                                    var videoFormat = GetVideoFormatFromMediaType(friendlyName,
                                        workingMediaType);
                                    // NV12 is not playable TODO check support for video formats
                                    if (videoFormat.SubType != "NV12")
                                        formatList.Add(videoFormat);
                                }
                        }
                    }
                }
            }
        }

        return formatList.OrderBy(format => format.SubType).ThenBy(format => format.FrameSizeHeight)
            .ThenBy(format => format.FrameRate);
    }

    private static IMFMediaSource GetMediaSourceFromVideoDevice(string symbolicLink)
    {
        using (var attributeContainer = MediaFactory.MFCreateAttributes(2))
        {
            attributeContainer.Set(CaptureDeviceAttributeKeys.SourceType,
                CaptureDeviceAttributeKeys.SourceTypeVidcap);

            attributeContainer.Set(CaptureDeviceAttributeKeys.SourceTypeVidcapSymbolicLink,
                symbolicLink);

            return MediaFactory.MFCreateDeviceSource(attributeContainer);
        }
    }

    private static Guid GetMajorMediaTypeFromPresentationDescriptor(
        IMFPresentationDescriptor presentationDescriptor,
        int streamIndex)
    {
        presentationDescriptor.GetStreamDescriptorByIndex(streamIndex, out _,
            out var streamDescriptor);

        using (streamDescriptor)
        {
            return GetMajorMediaTypeFromStreamDescriptor(streamDescriptor);
        }
    }

    private static Guid GetMajorMediaTypeFromStreamDescriptor(IMFStreamDescriptor streamDescriptor)
    {
        using (var pHandler = streamDescriptor.MediaTypeHandler)
        {
            var guidMajorType = pHandler.MajorType;

            return guidMajorType;
        }
    }

    private static IVideoFormat GetVideoFormatFromMediaType(string videoDeviceName, IMFMediaType mediaType)
    {
        // MF_MT_MAJOR_TYPE
        // Major type GUID, we return this as human readable text
        var majorType = mediaType.MajorType;

        // MF_MT_SUBTYPE
        // Subtype GUID which describes the basic media type, we return this as human readable text
        var subType = mediaType.Get<Guid>(MediaTypeAttributeKeys.Subtype);

        // MF_MT_FRAME_SIZE
        // the Width and height of a video frame, in pixels
        MediaFactory.MFGetAttributeSize(mediaType, MediaTypeAttributeKeys.FrameSize, out var frameSizeWidth,
            out var frameSizeHeight);

        // MF_MT_FRAME_RATE
        // The frame rate is expressed as a ratio.The upper 32 bits of the attribute value contain the numerator and the lower 32 bits contain the denominator. 
        // For example, if the frame rate is 30 frames per second(fps), the ratio is 30 / 1.If the frame rate is 29.97 fps, the ratio is 30,000 / 1001.
        // we report this back to the user as a decimal
        MediaFactory.MFGetAttributeRatio(mediaType, MediaTypeAttributeKeys.FrameRate, out var frameRate,
            out var frameRateDenominator);

        var videoFormat = new VideoFormat(videoDeviceName, majorType, subType, (int)frameSizeWidth,
            (int)frameSizeHeight,
            (int)frameRate,
            (int)frameRateDenominator);

        return videoFormat;
    }
}