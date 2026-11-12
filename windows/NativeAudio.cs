using System.Runtime.InteropServices;
namespace RadioIN;

// LibVLC is shipped beside the application, including its audio/HTTP plugins.
sealed class NativeAudio : IDisposable
{
    IntPtr instance, player;
    public NativeAudio()
    {
        instance = libvlc_new(0, IntPtr.Zero);
        if (instance == IntPtr.Zero) throw new InvalidOperationException("Initialisation du moteur audio impossible.");
        player = libvlc_media_player_new(instance);
        if (player == IntPtr.Zero) { libvlc_release(instance); instance = IntPtr.Zero; throw new InvalidOperationException("Lecteur audio indisponible."); }
    }
    public void Play(string url)
    {
        Stop();
        var media = libvlc_media_new_location(instance, url);
        if (media == IntPtr.Zero) throw new InvalidOperationException("Flux indisponible.");
        try { libvlc_media_add_option(media, ":network-caching=1500"); libvlc_media_player_set_media(player, media); }
        finally { libvlc_media_release(media); }
        if (libvlc_media_player_play(player) != 0) throw new InvalidOperationException("Lecture impossible.");
    }
    public int State => player == IntPtr.Zero ? 0 : libvlc_media_player_get_state(player);
    public long Time => player == IntPtr.Zero ? 0 : libvlc_media_player_get_time(player);
    public int VolumePercent => player == IntPtr.Zero ? -1 : libvlc_audio_get_volume(player);
    public void SetVolume(double value) { if (player != IntPtr.Zero) libvlc_audio_set_volume(player, (int)Math.Round(value * 100)); }
    public void Stop() { if (player != IntPtr.Zero) libvlc_media_player_stop(player); }
    public void Dispose() { Stop(); if (player != IntPtr.Zero) libvlc_media_player_release(player); if (instance != IntPtr.Zero) libvlc_release(instance); player = instance = IntPtr.Zero; }
    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr libvlc_new(int argc, IntPtr argv);
    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] static extern void libvlc_release(IntPtr instance);
    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr libvlc_media_player_new(IntPtr instance);
    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] static extern void libvlc_media_player_release(IntPtr player);
    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr libvlc_media_new_location(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string url);
    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] static extern void libvlc_media_add_option(IntPtr media, [MarshalAs(UnmanagedType.LPUTF8Str)] string option);
    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] static extern void libvlc_media_release(IntPtr media);
    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] static extern void libvlc_media_player_set_media(IntPtr player, IntPtr media);
    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] static extern int libvlc_media_player_play(IntPtr player);
    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] static extern void libvlc_media_player_stop(IntPtr player);
    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] static extern int libvlc_media_player_get_state(IntPtr player);
    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] static extern long libvlc_media_player_get_time(IntPtr player);
    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] static extern int libvlc_audio_set_volume(IntPtr player, int volume);
    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)] static extern int libvlc_audio_get_volume(IntPtr player);
}
