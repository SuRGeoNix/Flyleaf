using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Flyleaf;

internal sealed record LaunchRequest(string[] Inputs, bool StartMinimized, bool NewWindow)
{
    public static LaunchRequest Parse(IEnumerable<string> arguments, string workingDirectory, bool newWindow)
    {
        bool startMinimized = false;
        var inputs = new List<string>();
        foreach (var argument in arguments)
        {
            if (string.Equals(argument, "/StartMinimized", StringComparison.OrdinalIgnoreCase))
                startMinimized = true;
            else if (!string.IsNullOrWhiteSpace(argument))
            {
                if (Uri.TryCreate(argument, UriKind.Absolute, out var uri))
                    inputs.Add(uri.IsFile ? uri.LocalPath : argument);
                else
                    inputs.Add(Path.GetFullPath(argument, workingDirectory));
            }
        }
        return new(inputs.ToArray(), startMinimized, newWindow);
    }
}

/// <summary>Coordinates launches within one Windows user session.</summary>
internal sealed class SingleInstance : IDisposable
{
    const int MaxPayload = 256 * 1024;
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    readonly string name;
    readonly Mutex mutex;
    readonly CancellationTokenSource stop = new();
    Task? listener;
    bool ownsMutex;

    public SingleInstance(string name)
    {
        this.name = name;
        mutex = new Mutex(false, @"Local\" + name);
    }

    public static SingleInstance ForCurrentSession()
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        return new($"Flyleaf.{identity.User!.Value}.{process.SessionId}");
    }

    // The application's dispatcher owns and releases the mutex.
    public bool TryAcquire()
    {
        if (ownsMutex) return true;
        try { ownsMutex = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        return ownsMutex;
    }

    public void Listen(Func<LaunchRequest, bool> accept)
    {
        listener = ListenAsync(accept);
    }

    async Task ListenAsync(Func<LaunchRequest, bool> accept)
    {
        while (!stop.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await server.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                requestTimeout.CancelAfter(Timeout);
                var token = requestTimeout.Token;
                var header = new byte[sizeof(int)];
                await server.ReadExactlyAsync(header, token).ConfigureAwait(false);
                int length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length is <= 0 or > MaxPayload) continue;

                var payload = new byte[length];
                await server.ReadExactlyAsync(payload, token).ConfigureAwait(false);
                var request = JsonSerializer.Deserialize<LaunchRequest>(payload);
                byte accepted = request?.Inputs != null && request.Inputs.All(input => !string.IsNullOrWhiteSpace(input))
                    && accept(request) ? (byte)1 : (byte)0;
                await server.WriteAsync(new byte[] { accepted }, token).ConfigureAwait(false);
                // Wait for the client to read the acknowledgement and disconnect.
                await server.ReadAsync(header.AsMemory(0, 1), token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException)
            {
                // A disconnected or malformed client does not stop the listener.
            }
        }
    }

    public async Task<bool> ForwardAsync(LaunchRequest request)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(request);
        if (payload.Length > MaxPayload) return false;

        using var timeout = new CancellationTokenSource(Timeout);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
            if (GetNamedPipeServerProcessId(client.SafePipeHandle, out uint processId))
                AllowSetForegroundWindow(processId);

            var header = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            await client.WriteAsync(header, timeout.Token).ConfigureAwait(false);
            await client.WriteAsync(payload, timeout.Token).ConfigureAwait(false);
            await client.ReadExactlyAsync(header.AsMemory(0, 1), timeout.Token).ConfigureAwait(false);
            return header[0] == 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        try { listener?.GetAwaiter().GetResult(); }
        finally
        {
            stop.Dispose();
            if (ownsMutex) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    [DllImport("kernel32.dll")]
    static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(uint processId);
}
