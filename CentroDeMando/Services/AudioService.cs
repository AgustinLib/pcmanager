using System.Runtime.InteropServices;

namespace CentroDeMando.Services;

/// <summary>
/// Dispositivos de sonido de Windows (Core Audio): lista de salidas y entradas, volumen, silencio
/// y cambio del dispositivo predeterminado.
/// </summary>
public static class AudioService
{
    public sealed record AudioDevice(string Id, string Name, bool IsDefault, int Volume, bool Muted);

    // ───────────── COM ─────────────
    enum EDataFlow { Render = 0, Capture = 1 }
    enum ERole { Console = 0, Multimedia = 1, Communications = 2 }
    const int DEVICE_STATE_ACTIVE = 1;
    const int STGM_READ = 0;
    static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    static PropertyKey PKEY_Device_FriendlyName = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Explicit)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointer;
    }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant pv);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, int mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int i, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out int count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(int channel, float levelDb, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(int channel, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(int channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(int channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    /// <summary>Interfaz no documentada (pero estable desde Windows 7) que usa el propio panel de sonido.</summary>
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string id, IntPtr fmt);
        [PreserveSig] int GetDeviceFormat(string id, int def, IntPtr fmt);
        [PreserveSig] int ResetDeviceFormat(string id);
        [PreserveSig] int SetDeviceFormat(string id, IntPtr ep, IntPtr mix);
        [PreserveSig] int GetProcessingPeriod(string id, int def, IntPtr a, IntPtr b);
        [PreserveSig] int SetProcessingPeriod(string id, IntPtr p);
        [PreserveSig] int GetShareMode(string id, IntPtr mode);
        [PreserveSig] int SetShareMode(string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string id, IntPtr key, IntPtr pv);
        [PreserveSig] int SetPropertyValue(string id, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role);
        [PreserveSig] int SetEndpointVisibility(string id, int visible);
    }

    static readonly object Lock = new();

    static T Sta<T>(Func<T> fn)
    {
        // Core Audio funciona en cualquier apartamento, pero se ejecuta en un hilo propio para no
        // depender del hilo de trabajo que atiende la interfaz.
        T result = default!; Exception? err = null;
        var t = new Thread(() => { try { result = fn(); } catch (Exception e) { err = e; } });
        t.SetApartmentState(ApartmentState.STA); t.IsBackground = true; t.Start();
        if (!t.Join(8000)) throw new TimeoutException("El sistema de sonido no responde.");
        if (err != null) throw err;
        return result;
    }

    static IMMDeviceEnumerator Enumerator() => (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();

    static string Name(IMMDevice d)
    {
        if (d.OpenPropertyStore(STGM_READ, out var store) != 0) return "Dispositivo";
        var key = PKEY_Device_FriendlyName;
        if (store.GetValue(ref key, out var pv) != 0) return "Dispositivo";
        try { return pv.vt == 31 ? Marshal.PtrToStringUni(pv.pointer) ?? "Dispositivo" : "Dispositivo"; }
        finally { PropVariantClear(ref pv); }
    }

    static IAudioEndpointVolume? Volume(IMMDevice d)
    {
        var iid = IID_IAudioEndpointVolume;
        return d.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var o) == 0 ? o as IAudioEndpointVolume : null;
    }

    static List<AudioDevice> ListFlow(IMMDeviceEnumerator en, EDataFlow flow)
    {
        var list = new List<AudioDevice>();
        string? def = null;
        if (en.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out var dd) == 0) dd.GetId(out def);
        if (en.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, out var col) != 0) return list;
        col.GetCount(out int n);
        for (int i = 0; i < n; i++)
        {
            if (col.Item(i, out var d) != 0) continue;
            d.GetId(out var id);
            int vol = 0; bool muted = false;
            var v = Volume(d);
            if (v != null) { v.GetMasterVolumeLevelScalar(out float f); v.GetMute(out muted); vol = (int)Math.Round(f * 100); }
            list.Add(new AudioDevice(id, Name(d), id == def, vol, muted));
        }
        return list.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).ToList();
    }

    public static object List() => Sta<object>(() =>
    {
        lock (Lock)
        {
            var en = Enumerator();
            return new { outputs = ListFlow(en, EDataFlow.Render), inputs = ListFlow(en, EDataFlow.Capture) };
        }
    });

    public static bool SetVolume(string id, int? volume, bool? mute) => Sta(() =>
    {
        lock (Lock)
        {
            if (Enumerator().GetDevice(id, out var d) != 0) throw new InvalidOperationException("Ese dispositivo de sonido ya no está conectado.");
            var v = Volume(d) ?? throw new InvalidOperationException("Este dispositivo no permite cambiar el volumen.");
            var ctx = Guid.Empty;
            if (volume != null) Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(Math.Clamp(volume.Value, 0, 100) / 100f, ref ctx));
            if (mute != null) Marshal.ThrowExceptionForHR(v.SetMute(mute.Value, ref ctx));
            return true;
        }
    });

    public static bool SetDefault(string id) => Sta(() =>
    {
        lock (Lock)
        {
            if (Enumerator().GetDevice(id, out _) != 0) throw new InvalidOperationException("Ese dispositivo de sonido ya no está conectado.");
            var pc = (IPolicyConfig)new PolicyConfigClient();
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                Marshal.ThrowExceptionForHR(pc.SetDefaultEndpoint(id, role));
            return true;
        }
    });
}
