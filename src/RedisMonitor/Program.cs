using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using Elasticsearch.Net;
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
            IConnectionPool connectionPool;

            var elasticSearchUri = new Uri(_config["elasticsearchurl"]);
            if (elasticSearchUri.GetType() == typeof(string[]))
            {
                connectionPool = new SniffingConnectionPool(new[] {elasticSearchUri});
            }
            else
            {
                connectionPool = new SingleNodeConnectionPool(elasticSearchUri);
            }
            
            var settings = new ConnectionSettings(connectionPool);
            _elasticClient = new ElasticClient(settings);

            var metricService = new MetricService(_redisConnection, _elasticClient);

            metricService.GetInstanceMetrics(_config["metriclist"]);
            GetClusterMetrics();
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
