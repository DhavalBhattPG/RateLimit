using RateLimitAPI.Attr;
using System.Net;

namespace RateLimitAPI.Middleware
{
    public class ClientData
    {
        public DateTime ClientTimeStamp{ get; set; }
        public int RequestCount { get; set; }
    }

    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;

        protected static Dictionary<string, ClientData> keyValuePairs = new Dictionary<string, ClientData>();

        public RateLimitingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var endpoint = context.GetEndpoint();
            var decorator = endpoint?.Metadata.GetMetadata<LimitRequests>();

            if (decorator is null)
            {
                await _next(context);
                return;
            }

            var key = GenerateClientKey(context);
            var clientStatistics = GetClientDataByKey(key);

            if (clientStatistics != null)
            {
                if (DateTime.UtcNow < clientStatistics.ClientTimeStamp.AddSeconds(decorator.TimeWindow) &&
                clientStatistics.RequestCount >= decorator.MaxRequests)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                    return;
                }
                else if(DateTime.UtcNow > clientStatistics.ClientTimeStamp.AddSeconds(decorator.TimeWindow))
                {
                    clientStatistics.ClientTimeStamp = DateTime.UtcNow;
                    clientStatistics.RequestCount = 0;
                }
                else
                {
                    clientStatistics.RequestCount += 1;
                }

                await UpdateClientData(key, clientStatistics);
            }
            
            await _next(context);
        }

        private async Task<bool> UpdateClientData(string key, ClientData clientData)
        {
            if (keyValuePairs.ContainsKey(key))
            {
                keyValuePairs[key] = clientData;
            }
            else
            {
                keyValuePairs.Add(key, clientData);
            }
            return await Task.FromResult(true); 
        }

        private static string GenerateClientKey(HttpContext context)
            => $"{context.Connection.RemoteIpAddress}";

        private ClientData GetClientDataByKey(string key)
        {
            if (keyValuePairs.ContainsKey(key))
                return keyValuePairs[key];
            else
                return new ClientData() { ClientTimeStamp = DateTime.UtcNow, RequestCount = 0 };
        }


    }

    public static class RateLimitMiddlewareExtensions
    {
        public static IApplicationBuilder UseRateLimit(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<RateLimitingMiddleware>();
        }
    }
}
