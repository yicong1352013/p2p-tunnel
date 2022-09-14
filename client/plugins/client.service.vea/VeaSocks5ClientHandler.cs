﻿using client.messengers.clients;
using client.messengers.register;
using client.service.socks5;
using common.libs;
using common.libs.extends;
using common.server;
using common.socks5;
using System;
using System.Linq;
using System.Net;

namespace client.service.vea
{
    public interface IVeaSocks5ClientHandler : ISocks5ClientHandler
    {
    }

    public class VeaSocks5ClientHandler : Socks5ClientHandler, IVeaSocks5ClientHandler
    {
        private readonly IVeaSocks5MessengerSender socks5MessengerSender;
        private readonly Config config;
        private readonly IClientInfoCaching clientInfoCaching;
        private readonly VeaTransfer virtualEthernetAdapterTransfer;

        public VeaSocks5ClientHandler(IVeaSocks5MessengerSender socks5MessengerSender, RegisterStateInfo registerStateInfo, common.socks5.Config socks5Config, Config config, IClientInfoCaching clientInfoCaching, IVeaSocks5ClientListener socks5ClientListener, VeaTransfer virtualEthernetAdapterTransfer)
            : base(socks5MessengerSender, registerStateInfo, socks5Config, clientInfoCaching, socks5ClientListener)
        {
            this.socks5MessengerSender = socks5MessengerSender;
            this.config = config;
            this.clientInfoCaching = clientInfoCaching;
            this.virtualEthernetAdapterTransfer = virtualEthernetAdapterTransfer;
        }
        protected override void OnClose(Socks5Info info)
        {
            if (info.Tag is TagInfo target)
            {
                socks5MessengerSender.RequestClose(info.Id, target.Connection);
            }
        }
        protected override bool HandleCommand(Socks5Info data)
        {
            if ((data.Tag is TagInfo target) == false)
            {
                target = new TagInfo();
                data.Tag = target;
            }
            var targetEp = Socks5Parser.GetRemoteEndPoint(data.Data, out Span<byte> ipMemory);

            target.TargetIp = targetEp.Address;
            Logger.Instance.Info($"step:{string.Join(",",data.Data.Span.ToArray())}");
            Logger.Instance.Info($"command:{data.Data.Span[1]}, vea target:{targetEp}");
            if (targetEp.Port == 0)
            {
                data.Response[0] = (byte)Socks5EnumResponseCommand.ConnecSuccess;
                data.Data = data.Response;
                CommandResponseData(data);
                return true;
            }
            target.Connection = GetConnection(target.TargetIp, ipMemory);
            return socks5MessengerSender.Request(data, target.Connection);
        }
        protected override bool HndleForward(Socks5Info data)
        {
            TagInfo target = data.Tag as TagInfo;
            return socks5MessengerSender.Request(data, target.Connection);
        }
        protected override bool HndleForwardUdp(Socks5Info data)
        {
            IPEndPoint remoteEndPoint = Socks5Parser.GetRemoteEndPoint(data.Data, out Span<byte> ipMemory);

            Logger.Instance.Warning($"forward vea target:{remoteEndPoint}");

            IConnection connection = GetConnection(remoteEndPoint.Address, ipMemory);
            return socks5MessengerSender.Request(data, connection);
        }
        public override void Flush()
        {
        }

        private IConnection GetConnection(IPAddress target, Span<byte> ipMemory)
        {
            if (virtualEthernetAdapterTransfer.IPList.TryGetValue(target, out IPAddressCacheInfo cache))
            {
                return SelectConnection(cache.Client.TcpConnection, cache.Client.UdpConnection);
            }

            if (target.IsLan())
            {
                int mask = virtualEthernetAdapterTransfer.GetIpMask(ipMemory);
                if (virtualEthernetAdapterTransfer.LanIPList.TryGetValue(mask, out cache))
                {
                    return SelectConnection(cache.Client.TcpConnection, cache.Client.UdpConnection);
                }
            }

            var client = clientInfoCaching.GetByName(config.TargetName);
            if (client != null)
            {
                return SelectConnection(client.TcpConnection, client.UdpConnection);
            }
            return null;
        }
        private IConnection SelectConnection(IConnection tcpconnection, IConnection udpconnection)
        {
            return config.TunnelType switch
            {
                TunnelTypes.TCP_FIRST => tcpconnection != null ? tcpconnection : udpconnection,
                TunnelTypes.UDP_FIRST => udpconnection != null ? udpconnection : tcpconnection,
                TunnelTypes.TCP => tcpconnection,
                TunnelTypes.UDP => udpconnection,
                _ => tcpconnection,
            };
        }

        class TagInfo
        {
            public IConnection Connection { get; set; }
            public IPAddress TargetIp { get; set; }
        }
    }
}
