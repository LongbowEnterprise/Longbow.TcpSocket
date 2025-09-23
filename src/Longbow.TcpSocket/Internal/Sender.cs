// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;

namespace Longbow.TcpSocket;

sealed class Sender(Socket socket) : SocketAsyncEventArgs, IValueTaskSource<bool>
{
    private ManualResetValueTaskSourceCore<bool> _tcs;
    private int _length;
    private int _totalSent;
    private Memory<byte> _buffer;

    public ValueTask<bool> SendAsync(ReadOnlyMemory<byte> data)
    {
        _tcs.Reset();

        _length = data.Length;
        _totalSent = 0;
        _buffer = MemoryMarshal.AsMemory(data);
        SendCoreAsync();

        return new ValueTask<bool>(this, _tcs.Version);
    }

    private void SendCoreAsync()
    {
        int bytesToSend = Math.Min(_length - _totalSent, 1460);
        SetBuffer(_buffer.Slice(_totalSent, bytesToSend));

        if (!socket.SendAsync(this))
        {
            OnCompleted(this);
        }
    }

    protected override void OnCompleted(SocketAsyncEventArgs e)
    {
        if (e.SocketError == SocketError.Success)
        {
            _totalSent += e.BytesTransferred;
            if (_totalSent >= _length)
            {
                _tcs.SetResult(true);
            }
            else
            {
                SendCoreAsync();
            }
        }
        else
        {
            socket.Close();
            _tcs.SetException(new SocketException((int)e.SocketError));
        }
    }

    bool IValueTaskSource<bool>.GetResult(short token) => _tcs.GetResult(token);

    ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) => _tcs.GetStatus(token);

    void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _tcs.OnCompleted(continuation, state, token, flags);
}
