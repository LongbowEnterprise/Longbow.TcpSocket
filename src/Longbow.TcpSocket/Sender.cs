// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using System.Buffers;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Longbow.TcpSocket;

internal class Sender : IDisposable
{
    private readonly SocketAsyncEventArgs _args;
    private readonly System.Net.Sockets.Socket _socket;

    public Sender(System.Net.Sockets.Socket socket)
    {
        _socket = socket;
        _args = new();
        _args.Completed += OnSendCompleted;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken token = default)
    {
        var tcs = new TaskCompletionSource();
        var state = new SendState();
        try
        {
            if (MemoryMarshal.TryGetArray(data, out var segment))
            {
                state.Handle = data.Pin();
                _args.SetBuffer(segment.Array, segment.Offset, segment.Count);
            }
            else
            {
                _args.SetBuffer(MemoryMarshal.AsMemory(data));
            }

            _args.UserToken = (state, tcs);

            if (!_socket.SendAsync(_args))
            {
                OnSendCompleted(null, _args);
            }
        }
        catch (Exception ex)
        {
            state.Dispose();
            tcs.SetException(ex);
        }

        return new ValueTask(tcs.Task);
    }

    private void OnSendCompleted(object? sender, SocketAsyncEventArgs e)
    {
        var (state, tcs) = ((SendState, TaskCompletionSource))e.UserToken!;
        state.Dispose();

        if (e.SocketError == SocketError.Success)
        {
            tcs.TrySetResult();
        }
        else
        {
            tcs.TrySetException(new SocketException((int)e.SocketError));
        }
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public void Dispose()
    {
        _args.Dispose();
    }

    class SendState : IDisposable
    {
        public MemoryHandle Handle { get; set; }

        public void Dispose()
        {
            Handle.Dispose();
        }
    }
}
