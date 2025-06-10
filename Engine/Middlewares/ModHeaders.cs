using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace MatriX.API.Engine.Middlewares
{
    public class ModHeaders
    {
        private readonly RequestDelegate _next;
        public ModHeaders(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext)
        {
            httpContext.Response.Headers.Append("Access-Control-Allow-Credentials", "true");
            httpContext.Response.Headers.Append("Access-Control-Allow-Private-Network", "true");
            httpContext.Response.Headers.Append("Access-Control-Allow-Headers", "Accept, Origin, Content-Type, Authorization, X-Requested-With");
            httpContext.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, PUT, PATCH, DELETE, HEAD");

            if (httpContext.Request.Headers.TryGetValue("origin", out var origin))
                httpContext.Response.Headers.Append("Access-Control-Allow-Origin", origin.ToString());
            else if (httpContext.Request.Headers.TryGetValue("referer", out var referer))
                httpContext.Response.Headers.Append("Access-Control-Allow-Origin", referer.ToString());
            else
                httpContext.Response.Headers.Append("Access-Control-Allow-Origin", "*");

            httpContext.Response.Headers.Append("MatriX-API", "https://github.com/immisterio/MatriX.API");

            if (HttpMethods.IsOptions(httpContext.Request.Method))
            {
                if (httpContext.Request.Headers.TryGetValue("Access-Control-Request-Headers", out var AccessControl) && AccessControl == "authorization")
                    httpContext.Response.StatusCode = 204;

                return Task.CompletedTask;
            }

            return _next(httpContext);
        }
    }
}
