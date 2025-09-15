// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

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

    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        var tcs = new TaskCompletionSource<int>();
        var registration = token.Register(() => tcs.TrySetCanceled());

        _args.SetBuffer(buffer);
        _args.UserToken = (tcs, registration);

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
            tcs.TrySetException(ex);
        }

        return new ValueTask<int>(tcs.Task);
    }

    private void OnReceiveCompleted(object? sender, SocketAsyncEventArgs e)
    {
        var (tcs, registration) = ((TaskCompletionSource<int>, CancellationTokenRegistration))e.UserToken!;

        try
        {
            if (e.SocketError != SocketError.Success)
            {
                tcs.TrySetException(new SocketException((int)e.SocketError));
            }
            else if (e.BytesTransferred == 0)
            {
                _socket.Close();
                tcs.TrySetException(new SocketException((int)SocketError.ConnectionReset));
            }
            else
            {
                tcs.TrySetResult(e.BytesTransferred);
            }
        }
        finally
        {
            registration.Dispose();
        }
    }

    public void Dispose()
    {
        _args.Completed -= OnReceiveCompleted;
        _args.Dispose();
    }
}
