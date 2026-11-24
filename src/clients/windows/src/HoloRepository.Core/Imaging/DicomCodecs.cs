using FellowOakDicom;
using FellowOakDicom.Imaging.Codec;
using FellowOakDicom.Imaging.NativeCodec;
using Microsoft.Extensions.DependencyInjection;

namespace HoloRepository.Core.Imaging;

/// <summary>
/// Registers fo-dicom with the native codec package so compressed transfer syntaxes
/// (JPEG baseline/extended/lossless, JPEG-LS, JPEG 2000 and RLE) can be decoded.
/// </summary>
public static class DicomCodecs
{
    private static readonly Lazy<bool> Registered = new(Register, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>True when native codecs loaded; false means only uncompressed and RLE data can be read.</summary>
    public static bool NativeCodecsAvailable => Registered.Value;

    public static void EnsureRegistered() => _ = Registered.Value;

    private static bool Register()
    {
        try
        {
            var provider = new ServiceCollection()
                .AddFellowOakDicom()
                .AddTranscoderManager<NativeTranscoderManager>()
                .BuildServiceProvider();
            DicomSetupBuilder.UseServiceProvider(provider);
            var manager = provider.GetRequiredService<ITranscoderManager>();
            return manager.HasCodec(DicomTransferSyntax.JPEGProcess14SV1);
        }
        catch (Exception)
        {
            // Fall back to fo-dicom's built-in managed codecs.
            return false;
        }
    }
}
