using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Nest;
using StackExchange.Redis;

namespace RedisMonitor
{
    public class MetricService
    {
        private const string IndexStem = "redis-monitor-";
        private readonly IConnectionMultiplexer _redisConnection;
        private readonly IElasticClient _elasticClient;
        private readonly string _indexName = $"{IndexStem}{DateTime.UtcNow.ToString("yyyy.MM.dd")}";

        public MetricService(IConnectionMultiplexer redisConnection, IElasticClient elasticClient)
        {
            _redisConnection = redisConnection;
            _elasticClient = elasticClient;
        }

        public void GetInstanceMetrics(string metricList)
        {
            var redisEndpoints = _redisConnection.GetEndPoints()
                                                 .Where(e => e.AddressFamily != AddressFamily.Unspecified);

            var timeStamp = DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture);

            CreateTemplate();

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

            if (hits != 0)
                hitRate = hits / (hits + misses);

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

        private void CreateTemplate()
        {
            if (!_elasticClient.IndexTemplateExists(new IndexTemplateExistsRequest("redis")).Exists)
            {
                var request = new PutIndexTemplateRequest("redis")
                {
                    Template = $"{IndexStem}*",
                    Mappings = new Mappings(new Dictionary<TypeName, ITypeMapping> { { "_default_", new TypeMapping { NumericDetection = true } } })
                };

                _elasticClient.PutIndexTemplate(request);
            }
        }
    }
}
