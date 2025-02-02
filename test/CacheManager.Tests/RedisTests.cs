﻿using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using CacheManager.Core;
using CacheManager.Core.Configuration;
using CacheManager.Redis;
using FluentAssertions;
using Xunit;

namespace CacheManager.Tests
{
    /// <summary>
    /// To run the memcached test, run the bat files under /memcached before executing the tests.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class RedisTests
    {
        [Fact]
        [Trait("category", "Redis")]
        [Trait("category", "Unreliable")]
        public void Redis_Absolute_DoesExpire()
        {
            // arrange
            var item = new CacheItem<object>(Guid.NewGuid().ToString(), "something", ExpirationMode.Absolute, TimeSpan.FromMilliseconds(50));
            var cache = TestManagers.CreateRedisCache(1);

            // act/assert
            using (cache)
            {
                // act
                var result = cache.Add(item);

                // assert
                result.Should().BeTrue();
                Thread.Sleep(30);
                var value = cache.GetCacheItem(item.Key);
                value.Should().NotBeNull();

                Thread.Sleep(30);
                var valueExpired = cache.GetCacheItem(item.Key);
                valueExpired.Should().BeNull();
            }
        }

        [Fact]
        [Trait("category", "Redis")]
        [Trait("category", "Unreliable")]
        public void Redis_Absolute_DoesExpire_MultiClients()
        {
            // arrange
            var cacheA = TestManagers.CreateRedisCache(2);
            var cacheB = TestManagers.CreateRedisCache(2);

            // act/assert
            using (cacheA)
            using (cacheB)
            {
                // act
                var item = new CacheItem<object>(Guid.NewGuid().ToString(), "something", ExpirationMode.Absolute, TimeSpan.FromMilliseconds(50));

                var result = cacheA.Add(item);

                var itemB = cacheB.GetCacheItem(item.Key);

                // assert
                result.Should().BeTrue();
                item.Value.Should().Be(itemB.Value);

                Thread.Sleep(30);
                cacheA.GetCacheItem(item.Key).Should().NotBeNull();
                cacheB.GetCacheItem(item.Key).Should().NotBeNull();

                // after 210ms both it should be expired
                Thread.Sleep(30);
                cacheA.GetCacheItem(item.Key).Should().BeNull();
                cacheB.GetCacheItem(item.Key).Should().BeNull();
            }
        }

        [Fact]
        [Trait("category", "Redis")]
        [Trait("category", "Unreliable")]
        public void Redis_Multiple_PubSub_Change()
        {            
            // arrange
            string fileName = BaseCacheManagerTest.GetCfgFileName(@"/Configuration/configuration.valid.allFeatures.config");
            // redis config name must be same for all cache handles, configured via file and via code
            // otherwise the pub sub channel name is different
            string cacheName = "redisConfig";

            RedisConfigurations.LoadConfiguration(fileName, RedisConfigurationSection.DefaultSectionName);

            var cfg = ConfigurationBuilder.LoadConfigurationFile(fileName, cacheName);
            var cfgCache = CacheFactory.FromConfiguration<object>(cacheName, cfg);

            var item = new CacheItem<object>(Guid.NewGuid().ToString(), "something");

            // act/assert
            RedisTests.RunMultipleCaches(
                (cacheA, cacheB) =>
                {
                    cacheA.Put(item);
                    Thread.Sleep(10);
                    var value = cacheB.Get(item.Key);
                    value.Should().Be(item.Value, cacheB.ToString());
                    cacheB.Put(item.Key, "new value");
                },
                (cache) =>
                {
                    Thread.Sleep(10);
                    var value = cache.Get(item.Key);
                    value.Should().Be("new value", cache.ToString());
                }, 
                3,
                TestManagers.CreateRedisAndSystemCacheWithBackPlate(69),
                cfgCache,
                TestManagers.CreateRedisCache(69),
                TestManagers.CreateRedisAndSystemCacheWithBackPlate(69));
        }

        [Fact(Skip = "needs clear")]
        [Trait("category", "Redis")]
        public void Redis_Multiple_PubSub_Clear()
        {
            // arrange
            var item = new CacheItem<object>(Guid.NewGuid().ToString(), "something");

            // act/assert
            RedisTests.RunMultipleCaches(
                (cacheA, cacheB) =>
                {
                    cacheA.Add(item);
                    cacheB.Get(item.Key).Should().Be(item.Value);
                    cacheB.Clear();
                },
                (cache) =>
                {
                    cache.Get(item.Key).Should().BeNull();
                }, 
                10,
                TestManagers.CreateRedisAndSystemCacheWithBackPlate(4),
                TestManagers.CreateRedisAndSystemCacheWithBackPlate(4),
                TestManagers.CreateRedisCache(4),
                TestManagers.CreateRedisAndSystemCacheWithBackPlate(4));
        }

        [Fact]
        [Trait("category", "Redis")]
        [Trait("category", "Unreliable")]
        public void Redis_Multiple_PubSub_ClearRegion()
        {
            // arrange
            var item = new CacheItem<object>(Guid.NewGuid().ToString(), "something", Guid.NewGuid().ToString());

            // act/assert
            RedisTests.RunMultipleCaches(
                (cacheA, cacheB) =>
                {
                    cacheA.Add(item);
                    cacheB.Get(item.Key, item.Region).Should().Be(item.Value);
                    cacheB.ClearRegion(item.Region);
                },
                (cache) =>
                {
                    cache.Get(item.Key, item.Region).Should().BeNull();
                }, 10,
                TestManagers.CreateRedisAndSystemCacheWithBackPlate(5),
                TestManagers.CreateRedisAndSystemCacheWithBackPlate(5),
                TestManagers.CreateRedisCache(5),
                TestManagers.CreateRedisAndSystemCacheWithBackPlate(5));
        }

        [Fact]
        [Trait("category", "Redis")]
        public void Redis_Multiple_PubSub_Remove()
        {
            // arrange
            var item = new CacheItem<object>(Guid.NewGuid().ToString(), "something");

            // act/assert
            RedisTests.RunMultipleCaches(
                (cacheA, cacheB) =>
                {
                    cacheA.Add(item);
                    cacheB.Get(item.Key).Should().Be(item.Value);
                    cacheB.Remove(item.Key);
                },
                (cache) =>
                {
                    Thread.Sleep(10);
                    var value = cache.GetCacheItem(item.Key);
                    value.Should().BeNull();
                }, 
                2, 
                TestManagers.CreateRedisAndSystemCacheWithBackPlate(6), 
                TestManagers.CreateRedisAndSystemCacheWithBackPlate(6), 
                TestManagers.CreateRedisCache(6), 
                TestManagers.CreateRedisAndSystemCacheWithBackPlate(6));
        }

        [Fact]
        [Trait("category", "Redis")]
        [Trait("category", "Unreliable")]
        public void Redis_NoRaceCondition_WithUpdate()
        {
            using (var cache = CacheFactory.Build<RaceConditionTestElement>("myCache", settings =>
            {
                settings.WithUpdateMode(CacheUpdateMode.Full)
                    .WithRedisCacheHandle("default")
                    .WithExpiration(ExpirationMode.Absolute, TimeSpan.FromMinutes(20));
                settings.WithRedisConfiguration("default", config =>
                {
                    config.WithAllowAdmin()
                        .WithDatabase(7)
                        .WithEndpoint("127.0.0.1", 6379);
                });
            }))
            {
                var key = Guid.NewGuid().ToString();
                cache.Remove(key);
                cache.Add(key, new RaceConditionTestElement() { Counter = 0 });
                int numThreads = 5;
                int iterations = 10;
                int numInnerIterations = 10;
                int countCasModifyCalls = 0;

                // act
                ThreadTestHelper.Run(() =>
                {
                    for (int i = 0; i < numInnerIterations; i++)
                    {
                        cache.Update(key, (value) =>
                        {
                            value.Counter++;
                            Interlocked.Increment(ref countCasModifyCalls);
                            return value;
                        });
                    }
                }, numThreads, iterations);

                // assert
                Thread.Sleep(10);
                var result = cache.Get(key);
                result.Should().NotBeNull();
                Trace.TraceInformation("Counter increased to " + result.Counter + " cas calls needed " + countCasModifyCalls);
                result.Counter.Should().Be(numThreads * numInnerIterations * iterations, "counter should be exactly the expected value");
                countCasModifyCalls.Should().BeGreaterThan((int)result.Counter, "we expect many version collisions, so cas calls should be way higher then the count result");
            }
        }

        [Fact]
        [Trait("category", "Redis")]
        [Trait("category", "Unreliable")]
        public void Redis_RaceCondition_WithoutUpdate()
        {
            using (var cache = CacheFactory.Build<RaceConditionTestElement>("myCache", settings =>
            {
                settings.WithUpdateMode(CacheUpdateMode.Full)
                    .WithRedisCacheHandle("default")
                    .WithExpiration(ExpirationMode.Absolute, TimeSpan.FromMinutes(20));
                settings.WithRedisConfiguration("default", config =>
                {
                    config.WithAllowAdmin()
                        .WithDatabase(8)
                        .WithEndpoint("127.0.0.1", 6379);
                });
            }))
            {
                var key = Guid.NewGuid().ToString();
                cache.Add(key, new RaceConditionTestElement() { Counter = 0 });
                int numThreads = 5;
                int iterations = 10;
                int numInnerIterations = 10;

                // act
                ThreadTestHelper.Run(() =>
                {
                    for (int i = 0; i < numInnerIterations; i++)
                    {
                        var val = cache.Get(key);
                        val.Should().NotBeNull();
                        val.Counter++;

                        cache.Put(key, val);
                    }
                }, numThreads, iterations);

                // assert
                Thread.Sleep(10);
                var result = cache.Get(key);
                result.Should().NotBeNull();
                Trace.TraceInformation("Counter increased to " + result.Counter);
                result.Counter.Should().NotBe(numThreads * numInnerIterations * iterations);
            }
        }

        [Fact]
        [Trait("category", "Redis")]
        [Trait("category", "Unreliable")]
        public void Redis_Sliding_DoesExpire()
        {
            // arrange
            var item = new CacheItem<object>(Guid.NewGuid().ToString(), "something", ExpirationMode.Sliding, TimeSpan.FromMilliseconds(50));
            var cache = TestManagers.CreateRedisCache(9);

            // act/assert
            using (cache)
            {
                // act
                var result = cache.Add(item);

                // assert
                result.Should().BeTrue();

                // 450ms added so absolute would be expired on the 2nd go
                for (int s = 0; s < 3; s++)
                {
                    Thread.Sleep(30);
                    var value = cache.GetCacheItem(item.Key);
                    value.Should().NotBeNull();
                }

                Thread.Sleep(60);
                var valueExpired = cache.GetCacheItem(item.Key);
                valueExpired.Should().BeNull();
            }
        }

        [Fact]
        [Trait("category", "Redis")]
        [Trait("category", "Unreliable")]
        public void Redis_Sliding_DoesExpire_MultiClients()
        {
            // arrange
            var item = new CacheItem<object>(Guid.NewGuid().ToString(), "something", ExpirationMode.Sliding, TimeSpan.FromMilliseconds(50));
            var cacheA = TestManagers.CreateRedisAndSystemCacheWithBackPlate(10);
            var cacheB = TestManagers.CreateRedisAndSystemCacheWithBackPlate(10);

            // act/assert
            using (cacheA)
            using (cacheB)
            {
                // act
                var result = cacheA.Add(item);

                var valueB = cacheB.Get(item.Key);

                // assert
                result.Should().BeTrue();
                item.Value.Should().Be(valueB);

                // 450ms added so absolute would be expired on the 2nd go
                for (int s = 0; s < 3; s++)
                {
                    Thread.Sleep(40);
                    cacheA.GetCacheItem(item.Key).Should().NotBeNull();
                    cacheB.GetCacheItem(item.Key).Should().NotBeNull();
                }

                Thread.Sleep(100);
                cacheA.GetCacheItem(item.Key).Should().BeNull();
                cacheB.GetCacheItem(item.Key).Should().BeNull();
            }
        }

        [Fact]
        [Trait("category", "Redis")]
        [Trait("category", "Unreliable")]
        public void Redis_Sliding_DoesExpire_WithRegion()
        {
            // arrange
            var item = new CacheItem<object>(Guid.NewGuid().ToString(), "something", "region", ExpirationMode.Sliding, TimeSpan.FromMilliseconds(50));
            var cache = TestManagers.CreateRedisCache(11);

            // act/assert
            using (cache)
            {
                // act
                var result = cache.Add(item);

                // assert
                result.Should().BeTrue();

                // 450ms added so absolute would be expired on the 2nd go
                for (int s = 0; s < 3; s++)
                {
                    Thread.Sleep(30);
                    var value = cache.GetCacheItem(item.Key, item.Region);
                    value.Should().NotBeNull();
                }

                Thread.Sleep(60);
                var valueExpired = cache.GetCacheItem(item.Key, item.Region);
                valueExpired.Should().BeNull();
            }
        }

        [Fact]
        [Trait("category", "Redis")]
        public void Redis_Valid_CfgFile_LoadWithRedisBackPlate()
        {
            // arrange
            string fileName = BaseCacheManagerTest.GetCfgFileName(@"/Configuration/configuration.valid.allFeatures.config");
            string cacheName = "redisConfig";

            // have to load the configuration manually because the file is not avialbale to the default ConfigurtaionManager
            RedisConfigurations.LoadConfiguration(fileName, RedisConfigurationSection.DefaultSectionName);

            // act
            var cfg = ConfigurationBuilder.LoadConfigurationFile(fileName, cacheName);
            var cache = CacheFactory.FromConfiguration<object>(cacheName, cfg);

            // assert
            cache.CacheHandles.Any(p => p.Configuration.IsBackPlateSource).Should().BeTrue();
        }

        [Fact]
        [Trait("category", "Redis")]
        public void Redis_LoadWithRedisBackPlate_FromAppConfig()
        {
            // RedisConfigurations should load this from default section from app.config

            // arrange
            string cacheName = "redisWithBackPlateAppConfig";

            // act
            var cfg = ConfigurationBuilder.LoadConfiguration(cacheName);
            var cache = CacheFactory.FromConfiguration<object>(cacheName, cfg);
            var handle = cache.CacheHandles.First(p => p.Configuration.IsBackPlateSource) as RedisCacheHandle<object>;
            // test running something on the redis handle, Count should be enough to test the connection
            Action count = () => { var x = handle.Count; };

            // assert            
            handle.Should().NotBeNull();
            count.ShouldNotThrow();
        }

        [Fact]
        [Trait("category", "Redis")]
        public void Redis_LoadWithRedisBackPlate_FromAppConfigConnectionStrings()
        {
            // RedisConfigurations should load this from AppSettings from app.config
            // arrange
            string cacheName = "redisWithBackPlateAppConfigConnectionStrings";

            // act
            var cfg = ConfigurationBuilder.LoadConfiguration(cacheName);
            var cache = CacheFactory.FromConfiguration<object>(cacheName, cfg);
            var handle = cache.CacheHandles.First(p => p.Configuration.IsBackPlateSource) as RedisCacheHandle<object>;
            // test running something on the redis handle, Count should be enough to test the connection
            Action count = () => { var x = handle.Count; };

            // assert            
            handle.Should().NotBeNull();
            count.ShouldNotThrow();
        }

        [Fact]
        [Trait("category", "Redis")]
        public void Redis_ValueConverter_ObjectCacheTypeConversion_Bool()
        {
            var cache = TestManagers.CreateRedisCache(12);

            // act/assert
            using (cache)
            {
                var value = true;
                var key = Guid.NewGuid().ToString();
                cache.Add(key, value);
                var result = (bool)cache.Get(key);
                value.Should().Be(result);
            }
        }

        [Fact]
        [Trait("category", "Redis")]
        public void Redis_ValueConverter_ObjectCacheTypeConversion_Bytes()
        {
            var cache = TestManagers.CreateRedisCache(13);

            // act/assert
            using (cache)
            {
                var value = new byte[] { 0, 1, 2, 3 };
                var key = Guid.NewGuid().ToString();
                cache.Add(key, value);
                var result = cache.Get(key) as byte[];
                value.Should().BeEquivalentTo(result);
            }
        }

        [Fact]
        [Trait("category", "Redis")]
        public void Redis_ValueConverter_ObjectCacheTypeConversion_Double()
        {
            var cache = TestManagers.CreateRedisCache(14);

            // act/assert
            using (cache)
            {
                var value = 0231.2d;
                var key = Guid.NewGuid().ToString();
                cache.Add(key, value);
                var result = (double)cache.Get(key);
                value.Should().Be(result);
            }
        }

        [Fact]
        [Trait("category", "Redis")]
        public void Redis_ValueConverter_ObjectCacheTypeConversion_Int32()
        {
            var cache = TestManagers.CreateRedisCache(15);

            // act/assert
            using (cache)
            {
                var key = Guid.NewGuid().ToString();
                var value = 1234;
                cache.Add(key, value);
                var result = (int)cache.Get(key);
                value.Should().Be(result);
            }
        }

        [Fact]
        [Trait("category", "Redis")]
        public void Redis_ValueConverter_ObjectCacheTypeConversion_Long()
        {
            var cache = TestManagers.CreateRedisCache(16);

            // act/assert
            using (cache)
            {
                var key = Guid.NewGuid().ToString();
                var value = 123456L;
                cache.Add(key, value);
                var result = (long)cache.Get(key);
                value.Should().Be(result);
            }
        }

        [Fact]
        [Trait("category", "Redis")]
        public void Redis_ValueConverter_ObjectCacheTypeConversion_Poco()
        {
            var cache = TestManagers.CreateRedisCache(17);

            // act/assert
            using (cache)
            {
                var key = Guid.NewGuid().ToString();
                var value = new Poco() { Id = 23, Something = "§asdad" };
                cache.Add(key, value);
                var result = (Poco)cache.Get(key);
                value.ShouldBeEquivalentTo(result);
            }
        }

        [Fact]
        [Trait("category", "Redis")]
        public void Redis_ValueConverter_ObjectCacheTypeConversion_String()
        {
            var cache = TestManagers.CreateRedisCache(18);

            // act/assert
            using (cache)
            {
                var key = Guid.NewGuid().ToString();
                var value = "some string";
                cache.Add(key, value);
                var result = cache.Get(key) as string;
                value.Should().Be(result);
            }
        }

        private static void RunMultipleCaches<TCache>(
            Action<TCache, TCache> stepA,
            Action<TCache> stepB,
            int iterations,
            params TCache[] caches)
            where TCache : ICacheManager<object>
        {
            for (int i = 0; i < iterations; i++)
            {
                Thread.Sleep(10);

                if (caches.Length == 1)
                {
                    stepA(caches[0], caches[0]);
                }
                else
                {
                    stepA(caches[0], caches[1]);
                }

                Thread.Sleep(10);

                foreach (var cache in caches)
                {
                    stepB(cache);
                }
            }

            foreach (var cache in caches)
            {
                cache.Dispose();
            }
        }
    }

    [Serializable]
    [ExcludeFromCodeCoverage]
    internal class Poco
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "For testing only")]
        public int Id { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "For testing only")]
        public string Something { get; set; }
    }
}