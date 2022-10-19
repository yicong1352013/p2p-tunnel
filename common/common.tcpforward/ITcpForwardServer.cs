﻿using common.libs;
using System;

namespace common.tcpforward
{
    /// <summary>
    /// tcp转发监听服务
    /// </summary>
    public interface ITcpForwardServer
    {
        public void Init(int numConnections, int receiveBufferSize);
        public void Start(int port, TcpForwardAliveTypes aliveType);
        public void Response(TcpForwardInfo model);
        public void Stop(int sourcePort);
        public void Stop();

        public Func<TcpForwardInfo,bool> OnRequest { get; set; }
        public SimpleSubPushHandler<ListeningChangeInfo> OnListeningChange { get; }
    }
   
}
