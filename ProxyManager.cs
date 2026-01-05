using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AccountChecker
{
    public class ProxyManager
    {
        private static readonly string[] PROXY_APIS = new[]
        {
            "https://api.proxyscrape.com/v2/?request=get&protocol=socks5&timeout=10000&country=all",
            "https://raw.githubusercontent.com/TheSpeedX/PROXY-List/master/socks5.txt",
            "https://raw.githubusercontent.com/ShiftyTR/Proxy-List/master/socks5.txt",
            "https://raw.githubusercontent.com/monosans/proxy-list/main/proxies/socks5.txt",
            "https://raw.githubusercontent.com/hookzof/socks5_list/master/proxy.txt",
            "https://raw.githubusercontent.com/mmpx12/proxy-list/master/socks5.txt"
        };

        public static async Task<List<string>> ScrapeProxiesAsync()
        {
            var proxies = new HashSet<string>();
            var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            var tasks = PROXY_APIS.Select(async api =>
            {
                try
                {
                    var response = await httpClient.GetAsync(api);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        
                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();
                            if (trimmed.Contains(':') && !trimmed.StartsWith("#"))
                            {
                                proxies.Add(trimmed);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error fetching proxies from {api}: {ex.Message}");
                }
            });

            await Task.WhenAll(tasks);
            return proxies.ToList();
        }

        public static async Task<bool> ValidateProxyAsync(string proxy, int timeoutSeconds = 5)
        {
            try
            {
                // For SOCKS5, we just check if the format is valid
                // Full validation would require a SOCKS5 library
                var parts = proxy.Split(':');
                if (parts.Length >= 2)
                {
                    return int.TryParse(parts[1], out _);
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<List<string>> ValidateProxiesAsync(List<string> proxies, int maxConcurrent = 50)
        {
            var validProxies = new List<string>();
            var semaphore = new System.Threading.SemaphoreSlim(maxConcurrent);
            var tasks = new List<Task>();

            foreach (var proxy in proxies)
            {
                await semaphore.WaitAsync();
                
                var task = Task.Run(async () =>
                {
                    try
                    {
                        if (await ValidateProxyAsync(proxy, 3))
                        {
                            lock (validProxies)
                            {
                                validProxies.Add(proxy);
                            }
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });
                
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            return validProxies;
        }

        public static string GetRandomProxy(List<string> proxies)
        {
            if (proxies == null || proxies.Count == 0)
                return string.Empty;
                
            return proxies[Random.Shared.Next(proxies.Count)];
        }
    }
}
