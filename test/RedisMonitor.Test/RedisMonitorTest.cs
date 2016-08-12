using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Moq;
using Nest;
using NUnit.Framework;
using StackExchange.Redis;

namespace RedisMonitor.Test
{
    [TestFixture]
    public class RedisMonitorTest
    {
        [Test]
        public void WritesCorrectMetricsToElasticSearch()
        {
            var redisConnection = new Mock<IConnectionMultiplexer>();
            var endpoint = new Mock<EndPoint>();
            endpoint.SetupGet(e => e.AddressFamily).Returns(AddressFamily.Unknown);
            var endpoints = new[] {endpoint.Object};
            redisConnection.Setup(c => c.GetEndPoints(It.IsAny<bool>())).Returns(endpoints);
            var redisServer = new Mock<IServer>();
            var serverInfoList = new List<KeyValuePair<string, string>> {new KeyValuePair<string, string>("keyspace_hits", "1"), new KeyValuePair<string, string>("keyspace_misses", "1") };
            var serverInfo = serverInfoList.GroupBy(g => g.Key).ToArray();

            redisServer.Setup(r => r.Info(It.IsAny<RedisValue>(), It.IsAny<CommandFlags>())).Returns(serverInfo);
            redisConnection.Setup(r => r.GetServer(endpoint.Object, It.IsAny<object>()))
                .Returns(redisServer.Object);

            var elasticClient = new Mock<IElasticClient>();
            var metricService = new MetricService(redisConnection.Object, elasticClient.Object);

            metricService.GetInstanceMetrics("keyspace_hits,keyspace_misses");

            redisConnection.Verify(r => r.GetServer(It.Is<EndPoint>(x => x.Equals(endpoint.Object)), null));
            elasticClient.Verify(e => e.Index(It.Is<Dictionary<string,string>>(x => x.Count == 5 && x["keyspace_hits"] == "1" && x["keyspace_misses"] == "1" && x["hit_rate"] == "0.5"), It.IsAny<Func<IndexDescriptor<Dictionary<string, string>>, IIndexRequest>>()));
        }
    }
}
