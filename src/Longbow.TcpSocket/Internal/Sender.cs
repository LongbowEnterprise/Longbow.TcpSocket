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

    public ValueTask<bool> SendAsync(ReadOnlyMemory<byte> data)
    {
        var tcs = new TaskCompletionSource<bool>();

        if (MemoryMarshal.TryGetArray(data, out var segment))
        {
            _args.UserToken = tcs;
            _args.SetBuffer(segment.Array, segment.Offset, segment.Count);

            if (!_socket.SendAsync(_args))
            {
                OnSendCompleted(null, _args);
            }
        }
        return new ValueTask<bool>(tcs.Task);
    }

    private void OnSendCompleted(object? sender, SocketAsyncEventArgs e)
    {
        var tcs = (TaskCompletionSource<bool>)e.UserToken!;

        if (e.SocketError == SocketError.Success)
        {
            tcs.TrySetResult(true);
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
