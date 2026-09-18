using System;
using System.Buffers;
using System.Runtime.InteropServices;
using ValveResourceFormat.Renderer.Audio;

namespace GUI.Linux.Audio;

/// <summary>
/// PulseAudio output device for the shared sound event mixer, using the PulseAudio simple API
/// (<c>libpulse-simple.so.0</c>), which PipeWire's Pulse server also serves. Interleaved 32-bit float
/// samples are written with a blocking call, so the mixer thread is paced by the device buffer.
/// </summary>
internal sealed class PulseAudioDevice : IAudioDevice
{
    private const string Library = "libpulse-simple.so.0";

    // pa_stream_direction_t / pa_sample_format_t values.
    private const int PaStreamPlayback = 1;
    private const int PaSampleFloat32Le = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct PaSampleSpec
    {
        public int Format;
        public uint Rate;
        public byte Channels;
    }

    // The string parameters are explicitly marshalled as UTF-8. The analyzer does not accept the
    // LPUTF8Str annotation and wants no string parameters at all, which is not an option here.
#pragma warning disable CA2101
    [DllImport(Library, EntryPoint = "pa_simple_new", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern IntPtr PaSimpleNew(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? server,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int dir,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? dev,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string streamName,
        ref PaSampleSpec spec,
        IntPtr channelMap,
        IntPtr attr,
        out int error);

    [DllImport(Library, EntryPoint = "pa_simple_write", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int PaSimpleWrite(IntPtr simple, byte[] data, nuint bytes, out int error);

    [DllImport(Library, EntryPoint = "pa_simple_free", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern void PaSimpleFree(IntPtr simple);
#pragma warning restore CA2101

    private IntPtr handle;
    private volatile bool disposed;

    /// <inheritdoc/>
    public int SampleRate { get; }

    /// <inheritdoc/>
    public int Channels => 2;

    /// <summary>Whether a PulseAudio stream was opened.</summary>
    public bool Available { get; }

    /// <summary>Why the device is unavailable, when <see cref="Available"/> is false.</summary>
    public string? ErrorMessage { get; }

    public PulseAudioDevice(int sampleRate = 48000)
    {
        SampleRate = sampleRate;

        var spec = new PaSampleSpec { Format = PaSampleFloat32Le, Rate = (uint)sampleRate, Channels = (byte)Channels };
        handle = PaSimpleNew(null, "Source 2 Viewer", PaStreamPlayback, null, "Scene audio", ref spec, IntPtr.Zero, IntPtr.Zero, out var error);

        if (handle == IntPtr.Zero)
        {
            ErrorMessage = $"PulseAudio is unavailable (pa_simple_new error {error}).";
            return;
        }

        Available = true;
    }

    /// <inheritdoc/>
    public void SubmitSamples(ReadOnlySpan<float> samples)
    {
        if (!Available || disposed || samples.IsEmpty)
        {
            return;
        }

        var byteCount = samples.Length * sizeof(float);
        var bytes = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            MemoryMarshal.AsBytes(samples).CopyTo(bytes);
            _ = PaSimpleWrite(handle, bytes, (nuint)byteCount, out _);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (handle != IntPtr.Zero)
        {
            PaSimpleFree(handle);
            handle = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }

    ~PulseAudioDevice()
    {
        if (handle != IntPtr.Zero)
        {
            PaSimpleFree(handle);
            handle = IntPtr.Zero;
        }
    }
}
