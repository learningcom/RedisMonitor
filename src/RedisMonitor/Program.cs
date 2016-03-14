﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        private static readonly string IndexName = string.Format("redis-monitor-{0}", DateTime.Now.ToString("yyyy.MM.dd"));

        public static void Main(string[] args)
        {
            _config = new ConfigurationBuilder().AddJsonFile("appsettings.json")
                .AddCommandLine(args)
                .SetBasePath("./")
                .Build();

            var redisEndpointString = _config["redisendpoints"] + ",allowAdmin=true";
            _redisConnection = ConnectionMultiplexer.Connect(redisEndpointString);
            
            var elasticSearchUri = new Uri(_config["elasticsearchurl"]);
            _elasticClient = new ElasticClient(elasticSearchUri);

            GetInstanceMetrics();
            GetClusterMetrics();
        }

        private static void GetInstanceMetrics()
        {
            var redisEndpoints = _redisConnection.GetEndPoints();
            var metricList = _config["metriclist"];
            var timeStamp = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);

            foreach (var endpoint in redisEndpoints)
            {
                var server = _redisConnection.GetServer(endpoint);
                var info = server.Info();
                var metrics = info.SelectMany(groups => groups).Where(x => metricList.Contains(x.Key)).ToDictionary(g => g.Key, g => g.Value);

                metrics.Add("@timestamp", timeStamp);
                metrics.Add("endpoint", ParseEndPoint(endpoint.ToString()));
                CalculateHitRate(metrics);
                ParseKeyspaceMetrics(metrics);

                _elasticClient.Index(metrics, i => i.Index(IndexName));
            }
        }

        private static void CalculateHitRate(Dictionary<string, string> rawMetrics)
        {
            decimal hits = int.Parse(rawMetrics["keyspace_hits"]);
            decimal misses = int.Parse(rawMetrics["keyspace_misses"]);
            decimal hitRate = hits/(hits + misses);
            rawMetrics.Add("hit_rate", hitRate.ToString(CultureInfo.InvariantCulture));
        }

        private static void ParseKeyspaceMetrics(Dictionary<string, string> rawMetrics)
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
            var timeStamp = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);

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

            clusterInfoDictionary.Add("clustername", _config["clustername"]);
            clusterInfoDictionary.Add("@timestamp", timeStamp);
            _elasticClient.Index(clusterInfoDictionary, i => i.Index(IndexName));
        }
    }
}
