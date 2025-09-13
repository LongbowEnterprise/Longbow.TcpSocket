// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using System.Buffers;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Longbow.TcpSocket;

sealed class Receiver : IDisposable
{
    private readonly Socket _socket;
    private readonly SocketAsyncEventArgs _args;

    public Receiver(Socket socket)
    {
        _socket = socket;
        _args = new();
        _args.Completed += OnReceiveCompleted;
    }

    public ValueTask<int> ReceiveAsync(ReadOnlyMemory<byte> data, CancellationToken token = default)
    {
        var state = new ReceiveState();
        var tcs = state.CompletionSource;
        var registration = token.Register(() => tcs.TrySetCanceled());

        if (MemoryMarshal.TryGetArray(data, out var segment))
        {
            state.Buffer = segment.Array;
            _args.SetBuffer(segment.Array, segment.Offset, segment.Count);
        }
        else
        {
            state.Buffer = ArrayPool<byte>.Shared.Rent(data.Length);
            _args.SetBuffer(state.Buffer, 0, data.Length);
        }

        _args.UserToken = (state, registration);

        try
        {
            if (!_socket.ReceiveAsync(_args))
            {
                OnReceiveCompleted(null, _args);
            }
        }
        catch (Exception ex)
        {
            _socket.Close();
            ReturnBuffer(state.Buffer);
            tcs.TrySetException(ex);
        }

        return new ValueTask<int>(tcs.Task);
    }

    private void OnReceiveCompleted(object? sender, SocketAsyncEventArgs e)
    {
        var (state, registration) = ((ReceiveState, CancellationTokenRegistration))e.UserToken!;

        try
        {
            if (e.SocketError != SocketError.Success)
            {
                state.CompletionSource.TrySetException(new SocketException((int)e.SocketError));
            }
            else if (e.BytesTransferred == 0)
            {
                _socket.Close();
                state.CompletionSource.TrySetException(new SocketException((int)SocketError.ConnectionReset));
            }
            else
            {
                // 如果使用了非原始缓冲区，则拷贝数据
                if (!ReferenceEquals(e.Buffer, state.Buffer))
                {
                    new Span<byte>(e.Buffer, e.Offset, e.BytesTransferred).CopyTo(state.Buffer);
                }

                state.CompletionSource.TrySetResult(e.BytesTransferred);
            }
        }
        finally
        {
            registration.Dispose();
            if (e.Buffer != null)
            {
                ReturnBuffer(e.Buffer);
            }
        }
    }

    private void ReturnBuffer(byte[] buffer)
    {
        if (!ReferenceEquals(buffer, _args.Buffer))
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Dispose()
    {
        _args.Dispose();
    }

    sealed class ReceiveState
    {
        [NotNull]
        public byte[]? Buffer { get; set; }

        public TaskCompletionSource<int> CompletionSource = new();
    }
}
