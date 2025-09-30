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

    public ValueTask<bool> SendAsync(ReadOnlyMemory<byte> data)
    {
        _tcs.Reset();

        var _buffer = MemoryMarshal.AsMemory(data);
        SetBuffer(_buffer);

        // 发送数据
        if (!socket.SendAsync(this))
        {
            OnCompleted(this);
        }

        return new ValueTask<bool>(this, _tcs.Version);
    }

    public ValueTask<bool> SendAsync(IList<ArraySegment<byte>> data)
    {
        _tcs.Reset();

        SetBuffer(null, 0, 0);
        BufferList = data;

        // 发送数据
        if (!socket.SendAsync(this))
        {
            OnCompleted(this);
        }

        return new ValueTask<bool>(this, _tcs.Version);
    }

    protected override void OnCompleted(SocketAsyncEventArgs e)
    {
        if (e.SocketError == SocketError.Success)
        {
            _tcs.SetResult(true);
        }
        else
        {
            socket.Close();
            _tcs.SetException(new SocketException((int)e.SocketError));
        }
    }

    #region IValueTaskSource Implementation
    bool IValueTaskSource<bool>.GetResult(short token) => _tcs.GetResult(token);

    ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) => _tcs.GetStatus(token);

    [ExcludeFromCodeCoverage]
    void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _tcs.OnCompleted(continuation, state, token, flags);
    #endregion
}
