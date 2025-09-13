// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Longbow.TcpSocket;

sealed class Sender : IDisposable
{
    private readonly Socket _socket;
    private readonly SocketAsyncEventArgs _args;

    public Sender(Socket socket)
    {
        _socket = socket;
        _args = new();
        _args.Completed += OnSendCompleted;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken token = default)
    {
        var tcs = new TaskCompletionSource();
        var registration = token.Register(() => tcs.TrySetCanceled());

        try
        {
            if (MemoryMarshal.TryGetArray(data, out var segment))
            {
                _args.SetBuffer(segment.Array, segment.Offset, segment.Count);
            }
            else
            {
                _args.SetBuffer(MemoryMarshal.AsMemory(data));
            }

            _args.UserToken = (tcs, registration);

            if (!_socket.SendAsync(_args))
            {
                OnSendCompleted(null, _args);
            }
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
        }

        return new ValueTask(tcs.Task);
    }

    private void OnSendCompleted(object? sender, SocketAsyncEventArgs e)
    {
        var (tcs, registration) = ((TaskCompletionSource, CancellationTokenRegistration))e.UserToken!;
        registration.Dispose();

        if (e.SocketError == SocketError.Success)
        {
            tcs.TrySetResult();
        }
        else
        {
            _socket.Close();
            tcs.TrySetException(new SocketException((int)e.SocketError));
        }
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public void Dispose()
    {
        _args.Completed -= OnSendCompleted;
        _args.Dispose();
    }
}
