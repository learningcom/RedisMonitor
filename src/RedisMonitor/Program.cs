using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Nest;
using StackExchange.Redis;

namespace RedisMonitor
{
    public class Program
    {
        private static ConnectionMultiplexer _redisConnection;
        private static IConfigurationRoot _config;
        private static ElasticClient _elasticClient;
        private static string _indexName; 

        public static void Main(string[] args)
        {
            _config = new ConfigurationBuilder().AddJsonFile("appsettings.json")
                .AddCommandLine(args)
                .SetBasePath(Path.GetFullPath("./"))
                .Build();

            _indexName = string.Format("redis-monitor-{0}", DateTime.UtcNow.ToString("yyyy.MM.dd"));

            var redisEndpointString = _config["redisendpoints"] + ",allowAdmin=true";
            _redisConnection = ConnectionMultiplexer.Connect(redisEndpointString);
            
            var elasticSearchUri = new Uri(_config["elasticsearchurl"]);
            _elasticClient = new ElasticClient(elasticSearchUri);

            GetInstanceMetrics();
            GetClusterMetrics();
        }

        private static void GetInstanceMetrics()
        {
            var redisEndpoints = _redisConnection.GetEndPoints()
                                                 .Where(e => e.AddressFamily != AddressFamily.Unspecified);

            var metricList = _config["metriclist"];
            var timeStamp = DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture);

            foreach (var endpoint in redisEndpoints)
            {
                var server = _redisConnection.GetServer(endpoint);
                var info = server.Info();
                var metrics = info.SelectMany(groups => groups)
                                  .Where(x => metricList.Contains(x.Key))
                                  .ToDictionary(g => g.Key, g => g.Value);

                metrics.Add("@timestamp", timeStamp);
                metrics.Add("endpoint", ParseEndPoint(endpoint.ToString()));
                CalculateHitRate(metrics);
                ParseKeyspaceMetrics(metrics);

                Console.WriteLine("Writing metrics for endpoint {0}", endpoint);

                _elasticClient.Index(metrics, i => i.Index(_indexName));
            }
        }

        private static void CalculateHitRate(IDictionary<string, string> rawMetrics)
        {
            decimal hits = int.Parse(rawMetrics["keyspace_hits"]);
            decimal misses = int.Parse(rawMetrics["keyspace_misses"]);
            decimal hitRate = 0;

            if(hits != 0)
                hitRate =  hits/(hits + misses);

            rawMetrics.Add("hit_rate", hitRate.ToString(CultureInfo.InvariantCulture));
        }

        private static void ParseKeyspaceMetrics(IDictionary<string, string> rawMetrics)
        {
            if (rawMetrics.ContainsKey("db0"))
            {
                var keySpaceString = rawMetrics["db0"];

                foreach (var keyspaceValue in keySpaceString.Split(','))
                {
                    var key = "keyspace_" + keyspaceValue.Split('=')[0];
                    var value = keyspaceValue.Split('=')[1];
                    rawMetrics.Add(key, value);
                }

                rawMetrics.Remove("db0");
            }
        }

        private static string ParseEndPoint(string rawEndpoint)
        {
            var index = rawEndpoint.IndexOf(":", StringComparison.Ordinal);
            return rawEndpoint.Substring(0, index);
        }

        private static void GetClusterMetrics()
        {
            var result = _redisConnection.GetDatabase().ScriptEvaluate("return redis.call('cluster','info')");
            var clusterInfo = result.ToString().Split('\n');
            var clusterInfoDictionary = new Dictionary<string, string>();
            var timeStamp = DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture);
            var clusterName = _config["clustername"];

            foreach (var info in clusterInfo)
            {
                if (!string.IsNullOrEmpty(info))
                {
                    var splitString = info.Split(':');
                    var index = splitString[1].IndexOf('\r');
                    if (index > 0)
                    {
                        var cleanValue = splitString[1].Remove(index);
                        clusterInfoDictionary.Add(splitString[0], cleanValue);
                    }
                }
            }

            clusterInfoDictionary.Add("clustername", clusterName);
            clusterInfoDictionary.Add("@timestamp", timeStamp);

            Console.WriteLine("Writing metrics for cluster {0}", clusterName);

            _elasticClient.Index(clusterInfoDictionary, i => i.Index(_indexName));
        }
    }
}
