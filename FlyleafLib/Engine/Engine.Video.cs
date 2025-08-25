﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Data;

using Vortice.DXGI;
using Vortice.MediaFoundation;

using FlyleafLib.MediaFramework.MediaDevice;

namespace FlyleafLib;

public class VideoEngine
{
    /// <summary>
    /// List of Video Capture Devices
    /// </summary>
    public ObservableCollection<VideoDevice>
                            CapDevices          { get; set; } = [];
    
    /// <summary>
    /// List of GPU Adpaters <see cref="Config.VideoConfig.GPUAdapter"/>
    /// </summary>
    public Dictionary<long, GPUAdapter>
                            GPUAdapters         { get; private set; }

    /// <summary>
    /// List of GPU Outputs from default GPU Adapter (Note: will no be updated on screen connect/disconnect)
    /// </summary>
    public List<GPUOutput>  Screens             { get; private set; } = [];
    public float            RecommendedLuminance{ get; set; }

    internal IDXGIFactory2  Factory;

    private readonly object lockCapDevices = new();

    internal VideoEngine()
    {
        if (DXGI.CreateDXGIFactory1(out Factory).Failure)
            throw new InvalidOperationException("Cannot create IDXGIFactory1");

        BindingOperations.EnableCollectionSynchronization(CapDevices, lockCapDevices);
        GPUAdapters = GetAdapters();
    }

    public void RefreshCapDevices()
    {
        lock (lockCapDevices)
        {
            Engine.Video.CapDevices.Clear();

            var devices = MediaFactory.MFEnumVideoDeviceSources();
                foreach (var device in devices)
                try { Engine.Video.CapDevices.Add(new VideoDevice(device.FriendlyName, device.SymbolicLink)); } catch(Exception) { }
        }
    }

    private Dictionary<long, GPUAdapter> GetAdapters()
    {
        Dictionary<long, GPUAdapter> adapters = [];

        string dump = "";

        for (uint i=0; Factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            bool hasOutput = false;

            List<GPUOutput> outputs = [];

            int maxHeight = 0;
            for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
            {
                IDXGIOutput6 output6 = null;
                GPUOutput gpout;

                if (Environment.OSVersion.Version.Major >= 10)
                    try { output6 = output.QueryInterface<IDXGIOutput6>(); } catch { }

                if (output6 != null)
                {
                    var outdesc = output6.Description1;
                
                    gpout = new()
                    {
                        Id          = GPUOutput.GPUOutputIdGenerator++,
                        DeviceName  = outdesc.DeviceName,
                        Left        = outdesc.DesktopCoordinates.Left,
                        Top         = outdesc.DesktopCoordinates.Top,
                        Right       = outdesc.DesktopCoordinates.Right,
                        Bottom      = outdesc.DesktopCoordinates.Bottom,
                        IsAttached  = outdesc.AttachedToDesktop,
                        Rotation    = (int)outdesc.Rotation,
                        MaxLuminance= outdesc.MaxLuminance
                    };

                    output6.Dispose();
                }
                else
                {
                    var outdesc = output.Description;
                
                    gpout = new()
                    {
                        Id          = GPUOutput.GPUOutputIdGenerator++,
                        DeviceName  = outdesc.DeviceName,
                        Left        = outdesc.DesktopCoordinates.Left,
                        Top         = outdesc.DesktopCoordinates.Top,
                        Right       = outdesc.DesktopCoordinates.Right,
                        Bottom      = outdesc.DesktopCoordinates.Bottom,
                        IsAttached  = outdesc.AttachedToDesktop,
                        Rotation    = (int)outdesc.Rotation
                    };
                }

                if (maxHeight < gpout.Height)
                    maxHeight = gpout.Height;

                outputs.Add(gpout);

                if (gpout.IsAttached)
                {
                    hasOutput = true;
                    if (gpout.MaxLuminance > 0 && (RecommendedLuminance == 0 || gpout.MaxLuminance < RecommendedLuminance))
                        RecommendedLuminance = gpout.MaxLuminance;
                }

                output.Dispose();
            }

            if (RecommendedLuminance == 0)
                RecommendedLuminance = 200;

            if (Screens.Count == 0 && outputs.Count > 0)
                Screens = outputs;

            var adapterdesc = adapter.Description1;
            adapters[adapterdesc.Luid] = new GPUAdapter()
            {
                SystemMemory    = adapterdesc.DedicatedSystemMemory.Value,
                VideoMemory     = adapterdesc.DedicatedVideoMemory.Value,
                SharedMemory    = adapterdesc.SharedSystemMemory.Value,
                Vendor          = VendorIdStr(adapterdesc.VendorId),
                Description     = adapterdesc.Description,
                Id              = adapterdesc.DeviceId,
                Luid            = adapterdesc.Luid,
                MaxHeight       = maxHeight,
                HasOutput       = hasOutput,
                Outputs         = outputs
            };

            dump += $"[#{i+1}] {adapters[adapterdesc.Luid]}\r\n";

            adapter.Dispose();
        }

        Engine.Log.Info($"GPU Adapters\r\n{dump}");

        return adapters;
    }

    // Use instead System.Windows.Forms.Screen.FromPoint
    public GPUOutput GetScreenFromPosition(int top, int left)
    {
        foreach(var screen in Screens)
        {
            if (top >= screen.Top && top <= screen.Bottom && left >= screen.Left && left <= screen.Right)
                return screen;
        }

        return null;
    }

    private static string VendorIdStr(uint vendorId)
    {
        switch (vendorId)
        {
            case 0x1002:
                return "ATI";
            case 0x10DE:
                return "NVIDIA";
            case 0x1106:
                return "VIA";
            case 0x8086:
                return "Intel";
            case 0x5333:
                return "S3 Graphics";
            case 0x4D4F4351:
                return "Qualcomm";
            default:
                return "Unknown";
        }
    }
}
