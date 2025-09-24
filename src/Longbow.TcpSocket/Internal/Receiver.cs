// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using System.Net.Sockets;
using System.Threading.Tasks.Sources;

namespace Longbow.TcpSocket;

sealed class Receiver(Socket socket) : SocketAsyncEventArgs, IValueTaskSource<int>
{
    private ManualResetValueTaskSourceCore<int> _tcs;

    public ValueTask<int> ReceiveAsync(Memory<byte> buffer)
    {
        _tcs.Reset();
        SetBuffer(buffer);

        if (!socket.ReceiveAsync(this))
        {
            OnCompleted(this);
        }

        return new ValueTask<int>(this, _tcs.Version);
    }

    protected override void OnCompleted(SocketAsyncEventArgs e)
    {
        if (e.SocketError != SocketError.Success)
        {
            _tcs.SetException(new SocketException((int)e.SocketError));
        }
        else if (e.BytesTransferred == 0)
        {
            socket.Close();
            _tcs.SetException(new SocketException((int)SocketError.ConnectionReset));
        }
        else
        {
            _tcs.SetResult(e.BytesTransferred);
        }
    }

    int IValueTaskSource<int>.GetResult(short token) => _tcs.GetResult(token);

    ValueTaskSourceStatus IValueTaskSource<int>.GetStatus(short token) => _tcs.GetStatus(token);

    void IValueTaskSource<int>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _tcs.OnCompleted(continuation, state, token, flags);
}
