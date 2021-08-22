﻿using common;
using common.extends;
using server.model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace server.packet
{

    /// <summary>
    /// 自定义UDP数据包
    /// 包的数据顺序
    /// int type
    /// int sequence length
    /// string sequence
    /// int index
    /// int total
    /// byte[] 数据
    /// </summary>
    public class UdpPacket : IPacket
    {
        private static readonly ConcurrentDictionary<long, ConcurrentDictionary<long, PacketCacheModel>> packetChunkCache =
            new ConcurrentDictionary<long, ConcurrentDictionary<long, PacketCacheModel>>();

        public long Sequence { get; set; }
        public int Total { get; set; }
        public int Index { get; set; }
        public byte[] Chunk { get; set; }

        public MessageTypes Type { get; set; }

        private UdpPacket()
        {

        }
        public UdpPacket(long sequence, int total, int index, byte[] chunk, MessageTypes type) : this()
        {
            Sequence = sequence;
            Total = total;
            Index = index;
            Chunk = chunk;
            Type = type;
        }

        public byte[] ToArray()
        {
            byte[] sequenceArray = BitConverter.GetBytes(Sequence);
            byte[] typeArray = BitConverter.GetBytes((int)Type);
            byte[] indexArray = BitConverter.GetBytes(Index);
            byte[] totalArray = BitConverter.GetBytes(Total);

            byte[] dist = new byte[Chunk.Length + typeArray.Length + indexArray.Length + sequenceArray.Length + totalArray.Length];

            int distIndex = 0;

            Array.Copy(typeArray, 0, dist, distIndex, typeArray.Length);
            distIndex += typeArray.Length;

            Array.Copy(sequenceArray, 0, dist, distIndex, sequenceArray.Length);
            distIndex += sequenceArray.Length;

            Array.Copy(indexArray, 0, dist, distIndex, indexArray.Length);
            distIndex += indexArray.Length;

            Array.Copy(totalArray, 0, dist, distIndex, totalArray.Length);
            distIndex += totalArray.Length;

            Array.Copy(Chunk, 0, dist, distIndex, Chunk.Length);

            return dist;
        }


        public static UdpPacket FromArray(IPEndPoint ip, byte[] array)
        {
            return FromArray(ip.ToInt64(), array);
        }

        /// <summary>
        /// byte[]  转为包结构  可能为null，做好判断
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="array"></param>
        /// <returns></returns>
        public static UdpPacket FromArray(long ip, byte[] array)
        {
            int skipIndex = 0;
            int type = BitConverter.ToInt32(array.Skip(skipIndex).Take(4).ToArray());
            skipIndex += 4;

            long sequence = BitConverter.ToInt64(array.Skip(skipIndex).Take(8).ToArray());
            skipIndex += 8;

            int index = BitConverter.ToInt32(array.Skip(skipIndex).Take(4).ToArray());
            skipIndex += 4;

            int total = BitConverter.ToInt32(array.Skip(skipIndex).Take(4).ToArray());
            skipIndex += 4;

            byte[] chunk = array.Skip(skipIndex).ToArray();

            if (total == 1)
            {
                return new UdpPacket(sequence, total, index, chunk, (MessageTypes)type);
            }

            //ip 分类
            _ = packetChunkCache.TryGetValue(ip, out ConcurrentDictionary<long, PacketCacheModel> ipPackets);
            if (ipPackets == null)
            {
                ipPackets = new ConcurrentDictionary<long, PacketCacheModel>();
            }
            //ip下的序号
            _ = ipPackets.TryGetValue(sequence, out PacketCacheModel packets);
            if (packets == null)
            {
                packets = new PacketCacheModel();
            }
            packets.Time = Helper.GetTimeStamp();
            packets.Buffers.Add(new PacketCacheBufferModel { Index = index, Buffers = chunk });

            if (packets.Buffers.Count == total)
            {
                _ = ipPackets.Remove(sequence, out _);
                if (ipPackets.Values.Count <= 0)
                {
                    packetChunkCache.TryRemove(ip, out _);
                }

                IOrderedEnumerable<PacketCacheBufferModel> ceches = packets.Buffers.OrderBy(c => c.Index);
                int totalPageSize = ceches.Sum(c => c.Buffers.Length);
                byte[] dist = new byte[totalPageSize];

                int distIndex = 0;
                foreach (PacketCacheBufferModel item in ceches)
                {
                    Buffer.BlockCopy(item.Buffers, 0, dist, distIndex, item.Buffers.Length);
                    distIndex += item.Buffers.Length;
                }
                packets.Buffers.Clear();
                return new UdpPacket(sequence, total, index, dist, (MessageTypes)type);
            }
            else
            {
                _ = ipPackets.AddOrUpdate(sequence, packets, (k, v) => packets);

                long time = Helper.GetTimeStamp();
                foreach (KeyValuePair<long, ConcurrentDictionary<long, PacketCacheModel>> item in packetChunkCache)
                {
                    foreach (KeyValuePair<long, PacketCacheModel> item1 in item.Value)
                    {
                        if (time - item1.Value.Time > 100)
                        {
                            _ = item.Value.TryRemove(item1.Key, out _);
                        }
                    }

                    if (item.Value.Count == 0)
                    {
                        _ = packetChunkCache.TryRemove(item.Key, out _);
                    }
                }
            }

            return null;
        }
    }

    public class PacketCacheModel
    {
        public long Time { get; set; } = Helper.GetTimeStamp();
        public List<PacketCacheBufferModel> Buffers { get; set; } = new List<PacketCacheBufferModel>();
    }
    public class PacketCacheBufferModel
    {
        public int Index { get; set; } = 0;
        public byte[] Buffers { get; set; } = new byte[0];
    }
}
