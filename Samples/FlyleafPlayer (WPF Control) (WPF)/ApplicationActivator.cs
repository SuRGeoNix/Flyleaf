// https://github.com/novotnyllc/SingleInstanceHelper/tree/master

﻿using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace FlyleafPlayer;

public static class ApplicationActivator
{
    static Mutex                    _mutexApplication;
    static readonly object          _mutexLock = new();
    static bool                     _firstApplicationInstance;
    static NamedPipeServerStream    _namedPipeServerStream;
    static SynchronizationContext   _syncContext;
    static Action<string[]>         _otherInstanceCallback;

    static string mName = $"M_{Environment.UserDomainName}_{Environment.UserName}";
    static string pName = $"P_{Environment.UserDomainName}_{Environment.UserName}";

    public static bool LaunchOrReturn(Action<string[]> otherInstanceCallback, string[] args)
    {
        _otherInstanceCallback = otherInstanceCallback ?? throw new ArgumentNullException(nameof(otherInstanceCallback));

        if (IsApplicationFirstInstance())
        {
            _syncContext = SynchronizationContext.Current;
            NamedPipeServerCreateServer();
            return true;
        }
        else
        {
            NamedPipeClientSendOptions(new() { CommandLineArguments = [.. args] });
            return false;
        }
    }

    private static bool IsApplicationFirstInstance()
    {
        if (_mutexApplication == null)
            lock (_mutexLock)
                if (_mutexApplication == null)
                    _mutexApplication = new Mutex(true, mName, out _firstApplicationInstance);

        return _firstApplicationInstance;
    }

    private static void NamedPipeClientSendOptions(Payload namedPipePayload)
    {
        try
        {
            using var namedPipeClientStream = new NamedPipeClientStream(".", pName, PipeDirection.Out);
            namedPipeClientStream.Connect(3000);

            var ser = new DataContractJsonSerializer(typeof(Payload));
            ser.WriteObject(namedPipeClientStream, namedPipePayload);
        }
        catch (Exception) { }
    }

    private static void NamedPipeServerCreateServer()
    {
        _namedPipeServerStream = new NamedPipeServerStream(
            pName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        _namedPipeServerStream.BeginWaitForConnection(NamedPipeServerConnectionCallback, _namedPipeServerStream);
    }

    private static void NamedPipeServerConnectionCallback(IAsyncResult iAsyncResult)
    {
        try
        {
            _namedPipeServerStream.EndWaitForConnection(iAsyncResult);

            var ser = new DataContractJsonSerializer(typeof(Payload));
            var payload = (Payload)ser.ReadObject(_namedPipeServerStream);

            if (_syncContext != null)
                _syncContext.Post(_ => _otherInstanceCallback(payload.CommandLineArguments.ToArray()), null);
            else
                _otherInstanceCallback(payload.CommandLineArguments.ToArray());
        }
        catch (ObjectDisposedException) { return; }
        catch (Exception) { }
        finally { _namedPipeServerStream.Dispose(); }

        NamedPipeServerCreateServer();
    }
}

[DataContract]
internal class Payload
{
    [DataMember]
    public List<string> CommandLineArguments { get; set; } = [];
}
