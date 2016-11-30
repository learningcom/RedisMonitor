RedisMonitor
=============
RedisMonitor is a cross platform application written with .NET core that reads Redis metrics from a list of instances and writes them to an ElasticSearch data store.

Prequsites
==========
* .NET core runtime [instructions for installation here](https://www.microsoft.com/net/core).

Running RedisMonitor
=====================

Configuration
-------------
Redis Monitor can be configured using either an appsettings.json file in the directory that you are running RedisMonitor from, command line parameters, or a combination thereof.  There are four required parameters that must be set.
`--elasticsearchurl` is the endpoint for ElasticSearch  
`--metriclist` is the list of metrics(from the Redis INFO command) to retrieve and write to ElasticSearch.  
`--redisendpoints` is a comma seperated list of Redis endpoints to query for info, including port.  
`--clustername` the name of your cluster

### Sample appsettings.json file
	{
	  "elasticsearchurl": "http://10.0.1.101:9200",
	  "metriclist": "tcp_port,total_connections_received,total_commands_processed,total_net_input_bytes,total_net_output_bytes,process_id,instantaneous_ops_per_sec,evicted_keys,rejected_connections,keyspace_hits,keyspace_misses,used_memory,mem_fragmentation_ratio,blocked_clients,connected_clients,rdb_last_save_time,rdb_changes_since_last_save,master_link_down_since,connected_slaves,master_last_io_seconds_ago,db0",
	  "redisendpoints": "pdxdevcac001:6379,pdxdevcac001:6380,pdxdevcac002:6379,pdxdevcac002:6380,pdxdevcac003:6379,pdxdevcac003:6380",
	  "clustername": "cache"
	}

To Run RedisMonitor with command line parameters  
`dotnet /path/to/RedisMonitor.dll -- "--redisendpoints \"pdxdevcac001:6379,pdxdevcac001:6380,pdxdevcac002:6379,pdxdevcac002:6380,pdxdevcac003:6379,pdxdevcac003:6380\""`

